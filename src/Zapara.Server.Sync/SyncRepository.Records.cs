using Zapara.Contracts.Sync;

namespace Zapara.Server.Sync;

internal sealed partial class SyncRepository
{
    // Same canonical camelCase record as SyncJson; r is always a sync_records row.
    // Trim timestamp fractions to match the frozen canonical UTC converter.
    private const string RecordJson = """
        jsonb_build_object('entityType',r.entity_type,'entityId',r.entity_id,'revision',r.revision,
            'tombstone',r.tombstone,'changedAt',
            rtrim(rtrim(to_char(r.changed_at AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS.US'),'0'),'.') || 'Z',
            'value',r.payload)
        """;

    private async Task<SyncRecord?> RecordAsync(string type, Guid id)
    {
        await using var command = Command($"SELECT ({RecordJson})::text FROM {Schema}.sync_records r WHERE user_id=@p0 AND entity_type=@p1 AND entity_id=@p2", UserId, type, id);
        return await command.ExecuteScalarAsync(ct) is string json ? Parse<SyncRecord>(json) : null;
    }

    private async Task<(SyncRecord Record, SyncMetadata Metadata)> WriteAsync(SyncMetadata metadata, string type,
        Guid id, long previousRevision, bool deleted, SyncValue? value, Guid opId)
    {
        var nextRevision = checked(previousRevision + 1);
        var nextSequence = checked(metadata.CurrentSequence + 1);
        var record = new SyncRecord(type, id, nextRevision, deleted, Now, value);
        // Only the locked user row allocates sequence. No global sequence and no commit outside Accounts.
        var allocated = await ScalarAsync<long>($"""
            UPDATE {Schema}.sync_state SET sequence=sequence+1
            WHERE user_id=@p0 AND sequence=@p1 RETURNING sequence
            """, UserId, metadata.CurrentSequence);
        if (allocated != nextSequence) throw new InvalidOperationException("Нарушен порядок Sync.");
        var json = Json(record);
        if (previousRevision == 0)
        {
            await ExecuteAsync($"""
                INSERT INTO {Schema}.sync_records(user_id,entity_type,entity_id,revision,tombstone,changed_at,payload)
                VALUES(@p0,@p1,@p2,@p3,@p4,@p5,NULLIF(@p6::jsonb->'value','null'::jsonb))
                """, UserId, type, id, nextRevision, deleted, Now, json);
        }
        else
        {
            var updated = await ScalarAsync<long>($"""
                UPDATE {Schema}.sync_records SET revision=@p3,tombstone=@p4,changed_at=@p5,
                    payload=NULLIF(@p6::jsonb->'value','null'::jsonb)
                WHERE user_id=@p0 AND entity_type=@p1 AND entity_id=@p2 AND revision=@p7 AND NOT tombstone
                RETURNING revision
                """, UserId, type, id, nextRevision, deleted, Now, json, previousRevision);
            if (updated != nextRevision) throw new InvalidOperationException("Нарушена ревизия Sync.");
        }
        await ExecuteAsync($"""
            INSERT INTO {Schema}.sync_changes(user_id,sequence,record,op_id,created_at)
            VALUES(@p0,@p1,@p2::jsonb,@p3,@p4)
            """, UserId, allocated, json, opId, Now);
        return (record, new(metadata.SyncEpoch, allocated, metadata.MinAfterSequence));
    }
}
