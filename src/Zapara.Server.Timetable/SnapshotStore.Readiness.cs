using System.Data;
using System.Text.Json;
using Npgsql;

namespace Zapara.Server.Timetable;

public sealed partial class SnapshotStore
{
    /// <summary>Checks the current schema and a readable published payload, without leases, DDL or repair.</summary>
    public async Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using (var mode = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL search_path=pg_catalog", connection, transaction))
            await mode.ExecuteNonQueryAsync(ct);
        await VerifySchemaAsync(connection, transaction, 2, ct);
        await using var command = new NpgsqlCommand($"""
            SELECT s.payload::text,s.snapshot_id,s.fetched_at,s.published_at,s.source_modified_at,
                s.source_kind,s.source_url,s.source_sha256
            FROM {quotedSchema}.state st JOIN {quotedSchema}.snapshots s ON s.snapshot_id=st.current_snapshot_id
            WHERE st.singleton
            """, connection, transaction);
        SnapshotPayload? payload;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return false;
            payload = JsonSerializer.Deserialize<SnapshotPayload>(reader.GetString(0), JsonOptions);
        }
        command.CommandText = $"SELECT attempt_id,sequence,started_at,finished_at,status,error_code FROM {quotedSchema}.refresh_attempts LIMIT 1";
        await using (var reader = await command.ExecuteReaderAsync(ct)) await reader.ReadAsync(ct);
        if (payload?.Period is null || payload.Period.WeekCount != 2 || string.IsNullOrWhiteSpace(payload.Period.Title) || payload.Period.TimeZone != "Europe/Moscow"
            || payload.Groups.IsDefaultOrEmpty || payload.Lessons.IsDefaultOrEmpty) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in payload.Groups)
            if (group is null || string.IsNullOrWhiteSpace(group.Id) || string.IsNullOrWhiteSpace(group.Name)
                || group.LessonCount < 0 || !ids.Add(group.Id)) return false;
        var keys = new HashSet<(string, int, int, int)>();
        foreach (var item in payload.Lessons)
            if (item?.Value is not { } lesson || !ids.Contains(item.GroupId) || lesson.DayOfWeek is < 1 or > 7
                || lesson.Parity is < 0 or > 2 || lesson.Index <= 0 || string.IsNullOrWhiteSpace(lesson.SubjectRaw)
                || !TimeOnly.TryParseExact(lesson.TimeStart, "HH:mm", out _) || !TimeOnly.TryParseExact(lesson.TimeEnd, "HH:mm", out _)
                || !keys.Add((item.GroupId, lesson.DayOfWeek, lesson.Parity, lesson.Index))) return false;
        var counts = payload.Lessons.GroupBy(item => item.GroupId).ToDictionary(g => g.Key, g => g.Count());
        if (payload.Groups.Any(group => group.LessonCount != counts.GetValueOrDefault(group.Id))) return false;
        await transaction.CommitAsync(ct);
        return true;
    }
}
