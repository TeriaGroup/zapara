using Zapara.Contracts.Sync;

namespace Zapara.Server.Sync;

internal sealed partial class SyncRepository
{
    private async Task CleanManifestsAsync(Guid epoch)
        => await ExecuteAsync($"DELETE FROM {Schema}.sync_manifests WHERE user_id=@p0 AND (epoch<>@p1 OR expires_at<=@p2)", UserId, epoch, Now);

    internal async Task<SyncResyncManifest> BeginResyncAsync(SyncMetadata metadata)
    {
        await CleanManifestsAsync(metadata.SyncEpoch);
        var existing = await ManifestAsync(null);
        if (existing is not null) return existing;
        var id = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO {Schema}.sync_manifests(id,user_id,epoch,high_water,created_at,expires_at,item_count)
            VALUES(@p0,@p1,@p2,@p3,@p4,@p5,0)
            """, id, UserId, metadata.SyncEpoch, metadata.CurrentSequence, Now, Now.AddMinutes(10));
        // All rows stay inside PostgreSQL. Ordinals are stable even as later mutations advance the feed.
        await ExecuteAsync($"""
            INSERT INTO {Schema}.sync_manifest_items(manifest_id,ordinal,record)
            SELECT @p0,row_number() OVER (ORDER BY r.entity_type,r.entity_id),{RecordJson}
            FROM {Schema}.sync_records r WHERE r.user_id=@p1
            """, id, UserId);
        var count = await ScalarAsync<long>($"""
            UPDATE {Schema}.sync_manifests SET item_count=(SELECT count(*) FROM {Schema}.sync_manifest_items WHERE manifest_id=@p0)
            WHERE id=@p0 AND user_id=@p1 RETURNING item_count
            """, id, UserId);
        return new(id, metadata.SyncEpoch, metadata.CurrentSequence, Now, Now.AddMinutes(10), count);
    }

    private async Task<SyncResyncManifest?> ManifestAsync(Guid? id)
    {
        await using var command = id.HasValue
            ? Command($"SELECT id,epoch,high_water,created_at,expires_at,item_count FROM {Schema}.sync_manifests WHERE user_id=@p0 AND id=@p1", UserId, id.Value)
            : Command($"SELECT id,epoch,high_water,created_at,expires_at,item_count FROM {Schema}.sync_manifests WHERE user_id=@p0 ORDER BY created_at,id LIMIT 1", UserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2),
            reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4), reader.GetInt64(5)) : null;
    }

    internal async Task<SyncReadResult<SyncResyncPage>> ReadResyncPageAsync(SyncMetadata metadata, Guid id, long after, int limit)
    {
        if (id == Guid.Empty || after < 0) return SyncReadResult<SyncResyncPage>.Failure(400, "invalid_cursor");
        if (limit is < 1 or > SyncValidation.PageRecords) return SyncReadResult<SyncResyncPage>.Failure(400, "invalid_request");
        await CleanManifestsAsync(metadata.SyncEpoch);
        var manifest = await ManifestAsync(id);
        if (manifest is null) return SyncReadResult<SyncResyncPage>.Failure(410, "manifest_expired");
        if (after > manifest.ItemCount) return SyncReadResult<SyncResyncPage>.Failure(400, "invalid_cursor");
        var items = new List<SyncManifestItem>(limit);
        var next = after;
        // The envelope has fixed-size identity/time fields; this allowance exceeds its serialized size.
        var bytes = SyncJson.Serialize(manifest).Length + 512;
        await using var command = Command($"""
            SELECT i.ordinal,i.record::text FROM {Schema}.sync_manifest_items i
            JOIN {Schema}.sync_manifests m ON m.id=i.manifest_id
            WHERE m.user_id=@p0 AND m.id=@p1 AND i.ordinal>@p2 ORDER BY i.ordinal LIMIT @p3
            """, UserId, id, after, limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var item = new SyncManifestItem(reader.GetInt64(0), Parse<SyncRecord>(reader.GetString(1)));
            var size = SyncJson.Serialize(item).Length + 1;
            if (bytes + size > SyncValidation.PageBytes) break;
            items.Add(item);
            bytes += size;
            next = item.Ordinal;
        }
        return SyncReadResult<SyncResyncPage>.Success(new(manifest, after, next, next < manifest.ItemCount, items));
    }
}
