using Zapara.Contracts.Sync;

namespace Zapara.Server.Sync;

internal sealed partial class SyncRepository
{
    internal async Task<SyncReadResult<SyncChangesPage>> ChangesAsync(SyncMetadata metadata, Guid epoch, long after, int limit)
    {
        if (epoch == Guid.Empty || after < 0) return SyncReadResult<SyncChangesPage>.Failure(400, "invalid_cursor");
        if (limit is < 1 or > SyncValidation.PageRecords) return SyncReadResult<SyncChangesPage>.Failure(400, "invalid_request");
        if (epoch != metadata.SyncEpoch || after < metadata.MinAfterSequence)
            return SyncReadResult<SyncChangesPage>.Failure(410, "sync_reset");
        if (after > metadata.CurrentSequence) return SyncReadResult<SyncChangesPage>.Failure(400, "invalid_cursor");
        var changes = new List<SyncChange>(limit);
        var next = after;
        // An empty envelope bounds overhead; Int64 counters add at most 20 bytes. Reserve more conservatively.
        var bytes = SyncJson.Serialize(new SyncChangesPage(metadata, after, after, false, [])).Length + 128;
        await using var command = Command($"""
            SELECT sequence,op_id,record::text FROM {Schema}.sync_changes
            WHERE user_id=@p0 AND sequence>@p1 ORDER BY sequence LIMIT @p2
            """, UserId, after, limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var change = new SyncChange(reader.GetInt64(0), reader.GetGuid(1), Parse<SyncRecord>(reader.GetString(2)));
            var size = SyncJson.Serialize(change).Length + 1;
            if (bytes + size > SyncValidation.PageBytes) break;
            changes.Add(change);
            bytes += size;
            next = change.Sequence;
        }
        return SyncReadResult<SyncChangesPage>.Success(new(metadata, after, next, next < metadata.CurrentSequence, changes));
    }
}
