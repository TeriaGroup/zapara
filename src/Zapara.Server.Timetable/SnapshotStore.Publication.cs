using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace Zapara.Server.Timetable;

public sealed partial class SnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> PublishAsync(RefreshLease lease, ValidatedSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(snapshot);
        await lease.EnterAsync(this, ct);
        var committing = false;
        try
        {
            ValidateIdentity(snapshot);
            var bytes = snapshot.Source.Bytes.ToArray();
            var payload = JsonSerializer.Serialize(snapshot.Payload, JsonOptions);
            var id = Guid.NewGuid();
            var now = clock.GetUtcNow().ToUniversalTime();
            await using var transaction = await lease.Connection.BeginTransactionAsync(ct);
            await using var command = new NpgsqlCommand($"""
                INSERT INTO {quotedSchema}.snapshots(snapshot_id,attempt_id,payload,original_xml,
                    source_kind,source_url,source_sha256,fetched_at,published_at,source_modified_at)
                SELECT @id,@attempt,@payload,@bytes,@kind,@url,@hash,@fetched,@now,@modified
                FROM {quotedSchema}.refresh_attempts WHERE attempt_id=@attempt AND status='running'
                """, lease.Connection, transaction);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("attempt", lease.AttemptId);
            command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, payload);
            command.Parameters.AddWithValue("bytes", bytes);
            command.Parameters.AddWithValue("kind", snapshot.Source.SourceKind == SourceKind.File ? "file" : "http");
            command.Parameters.AddWithValue("url", NpgsqlDbType.Text, (object?)snapshot.Source.SourceUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("hash", snapshot.Source.SourceSha256);
            command.Parameters.AddWithValue("fetched", snapshot.Source.FetchedAtUtc);
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("modified", NpgsqlDbType.TimestampTz, (object?)snapshot.Source.SourceModifiedAt ?? DBNull.Value);
            RequireSingle(await command.ExecuteNonQueryAsync(ct));
            command.CommandText = $"UPDATE {quotedSchema}.state SET current_snapshot_id=@id WHERE singleton";
            RequireSingle(await command.ExecuteNonQueryAsync(ct));
            command.CommandText = $"""
                UPDATE {quotedSchema}.refresh_attempts SET status='success',finished_at=@now,error_code=NULL
                WHERE attempt_id=@attempt AND status='running'
                """;
            RequireSingle(await command.ExecuteNonQueryAsync(ct));
            ct.ThrowIfCancellationRequested();
            committing = true;
            await transaction.CommitAsync(ct);
            lease.Complete();
            return id;
        }
        catch (Exception error) when (committing && (error is not PostgresException postgres
            || postgres.SqlState.StartsWith("08", StringComparison.Ordinal) || postgres.SqlState is "57P01" or "57P02" or "57P03"))
        {
            // A lost COMMIT acknowledgement cannot be safely retried or recorded as a failed publication.
            lease.Complete();
            throw new StoreException(FailureCode.PublicationUnknown, lease.AttemptId);
        }
        catch (Exception error) when (IsDatabaseError(error))
        {
            throw new StoreException(FailureCode.DbUnavailable, lease.AttemptId);
        }
        finally { lease.Exit(); }
    }

    public async Task RecordFailedAttemptAsync(RefreshLease lease, FailureCode code, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var storedCode = code.ToStorageCode();
        await lease.EnterAsync(this, ct);
        try
        {
            await using var command = new NpgsqlCommand($"""
                UPDATE {quotedSchema}.refresh_attempts SET status=@status,error_code=@code,finished_at=@now
                WHERE attempt_id=@attempt AND status='running'
                """, lease.Connection);
            command.Parameters.AddWithValue("status", code == FailureCode.Abandoned ? "abandoned" : "failed");
            command.Parameters.AddWithValue("code", storedCode);
            command.Parameters.AddWithValue("now", clock.GetUtcNow().ToUniversalTime());
            command.Parameters.AddWithValue("attempt", lease.AttemptId);
            RequireSingle(await command.ExecuteNonQueryAsync(ct));
            lease.Complete();
        }
        catch (Exception error) when (IsDatabaseError(error)) { throw new StoreException(FailureCode.DbUnavailable, lease.AttemptId); }
        finally { lease.Exit(); }
    }

    private static void RequireSingle(int rows)
    {
        if (rows != 1) throw new StoreException(FailureCode.DbUnavailable);
    }

    private static void ValidateIdentity(ValidatedSnapshot snapshot)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<(string, int, int, int)>();
        if (snapshot.Source.Bytes.Length is < 1 or > 16777216 || snapshot.Groups.IsEmpty)
            throw new StoreException(FailureCode.SnapshotMalformed);
        foreach (var group in snapshot.Groups)
            if (string.IsNullOrWhiteSpace(group.Id) || !ids.Add(group.Id) || group.LessonCount < 0)
                throw new StoreException(FailureCode.SnapshotMalformed);
        foreach (var lesson in snapshot.Lessons)
            if (!ids.Contains(lesson.GroupId) || !keys.Add((lesson.GroupId, lesson.Value.DayOfWeek, lesson.Value.Parity, lesson.Value.Index)))
                throw new StoreException(FailureCode.SnapshotMalformed);
        var counts = snapshot.Lessons.GroupBy(lesson => lesson.GroupId).ToDictionary(group => group.Key, group => group.Count());
        if (snapshot.Groups.Any(group => group.LessonCount != counts.GetValueOrDefault(group.Id)))
            throw new StoreException(FailureCode.SnapshotMalformed);
    }
}
