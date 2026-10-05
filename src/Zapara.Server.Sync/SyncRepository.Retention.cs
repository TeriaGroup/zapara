using Zapara.Contracts.Sync;

namespace Zapara.Server.Sync;

internal sealed partial class SyncRepository
{
    private async Task<SyncMetadata> MaintainAsync(SyncMetadata metadata)
    {
        var cutoff = Now.AddDays(-90);
        var eligible = await ScalarAsync<bool>($"""
            SELECT EXISTS(SELECT FROM {Schema}.sync_receipts WHERE user_id=@p0 AND created_at<@p1)
                OR EXISTS(SELECT FROM {Schema}.sync_changes WHERE user_id=@p0 AND created_at<@p1)
                OR EXISTS(SELECT FROM {Schema}.sync_records WHERE user_id=@p0 AND tombstone AND changed_at<@p1)
            """, UserId, cutoff);
        if (eligible)
        {
            var prunedThrough = await ScalarAsync<long>($"SELECT COALESCE(max(sequence),0) FROM {Schema}.sync_changes WHERE user_id=@p0 AND created_at<@p1", UserId, cutoff);
            metadata = new(Guid.NewGuid(), metadata.CurrentSequence, Math.Max(metadata.MinAfterSequence, prunedThrough));
            // Rotate/invalidate BEFORE pruning. Return business errors as values so Accounts commits this maintenance.
            await ExecuteAsync($"""
                UPDATE {Schema}.sync_state SET epoch=@p1,min_after_sequence=@p2 WHERE user_id=@p0;
                DELETE FROM {Schema}.sync_manifests WHERE user_id=@p0
                """, UserId, metadata.SyncEpoch, metadata.MinAfterSequence);
            await ExecuteAsync($"""
                DELETE FROM {Schema}.sync_receipts WHERE user_id=@p0 AND created_at<@p1;
                DELETE FROM {Schema}.sync_records WHERE user_id=@p0 AND tombstone AND changed_at<@p1;
                DELETE FROM {Schema}.sync_changes WHERE user_id=@p0 AND created_at<@p1
                """, UserId, cutoff);
        }
        await ExecuteAsync($"UPDATE {Schema}.sync_state SET last_maintenance_at=@p1 WHERE user_id=@p0", UserId, Now);
        return metadata;
    }
}
