using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace Zapara.Server.Timetable;

public sealed partial class SnapshotStore
{
    public async Task<SnapshotRead?> ReadCurrentAsync(CancellationToken ct = default)
        => (await ReadSelectionAsync(null, ct)).Selected;

    public async Task<SnapshotRead?> ReadPinnedAsync(Guid snapshotId, CancellationToken ct = default)
        => (await ReadSelectionAsync(snapshotId, ct)).Selected;

    public async Task<SnapshotSelection> ReadSelectionAsync(Guid? snapshotId = null, CancellationToken ct = default)
    {
        try
        {
            // One statement: pointer, selected immutable payload, and refresh history share one MVCC snapshot.
            await using var command = dataSource.CreateCommand($"""
                SELECT st.current_snapshot_id, s.snapshot_id, s.payload::text, s.fetched_at, s.published_at,
                    s.source_modified_at,s.source_kind,s.source_url,s.source_sha256,
                    a.attempt_id,a.status,ok.finished_at,bad.finished_at,bad.error_code,
                    COALESCE(bad.sequence > COALESCE(ok.sequence,0),false),bad.status
                FROM {quotedSchema}.state st
                LEFT JOIN {quotedSchema}.snapshots s
                    ON st.current_snapshot_id IS NOT NULL AND s.snapshot_id=COALESCE(@id,st.current_snapshot_id)
                LEFT JOIN LATERAL (SELECT attempt_id,status FROM {quotedSchema}.refresh_attempts ORDER BY sequence DESC LIMIT 1) a ON true
                LEFT JOIN LATERAL (SELECT sequence,finished_at FROM {quotedSchema}.refresh_attempts WHERE status='success' ORDER BY sequence DESC LIMIT 1) ok ON true
                LEFT JOIN LATERAL (SELECT sequence,finished_at,error_code,status FROM {quotedSchema}.refresh_attempts
                    WHERE status IN ('failed','abandoned') ORDER BY sequence DESC LIMIT 1) bad ON true
                WHERE st.singleton
                """);
            command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, (object?)snapshotId ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new StoreException(FailureCode.DbUnavailable);
            var current = NullableValue<Guid>(reader, 0);
            var failureAfterSuccess = reader.GetBoolean(14);
            var refresh = new RefreshDto(NullableValue<Guid>(reader, 9), Text(reader, 10),
                Utc(reader, 11), Utc(reader, 12), Text(reader, 13), failureAfterSuccess && Text(reader, 15) == "abandoned");
            SnapshotRead? selected = null;
            if (!reader.IsDBNull(1))
            {
                var id = reader.GetGuid(1);
                var fetched = Utc(reader, 3)!.Value;
                var meta = new SnapshotMetaDto(id, fetched, Utc(reader, 4)!.Value, Utc(reader, 5),
                    reader.GetString(6), Text(reader, 7), reader.GetString(8),
                    clock.GetUtcNow() - fetched >= TimeSpan.FromHours(24) || failureAfterSuccess || id != current);
                var payload = JsonSerializer.Deserialize<SnapshotPayload>(reader.GetString(2), JsonOptions)
                    ?? throw new StoreException(FailureCode.DbUnavailable);
                selected = new SnapshotRead(payload, meta, refresh);
            }
            return new SnapshotSelection(current, selected, refresh);
        }
        catch (Exception error) when (IsDatabaseError(error) || error is JsonException)
        {
            throw new StoreException(FailureCode.DbUnavailable);
        }
    }

    private static string? Text(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static T? NullableValue<T>(NpgsqlDataReader reader, int ordinal) where T : struct
        => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);
    private static DateTimeOffset? Utc(NpgsqlDataReader reader, int ordinal)
        => NullableValue<DateTimeOffset>(reader, ordinal)?.ToUniversalTime();
}
