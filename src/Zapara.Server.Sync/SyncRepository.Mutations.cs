using System.Security.Cryptography;
using Zapara.Contracts.Sync;

namespace Zapara.Server.Sync;

internal sealed partial class SyncRepository
{
    internal async Task<SyncMutationResult> MutateAsync(SyncMetadata metadata, SyncMutation request)
    {
        if (request.SyncEpoch != metadata.SyncEpoch) return new(410, "sync_reset", metadata, null);
        var digest = SyncJson.Digest(request);
        var receipt = await ReceiptAsync(request.OpId);
        if (receipt is not null)
            return CryptographicOperations.FixedTimeEquals(receipt.Digest, digest)
                ? SyncJson.Parse<SyncMutationResult>(receipt.Body)
                : new(409, "op_id_reused", metadata, null);

        var current = await RecordAsync(request.EntityType, request.EntityId);
        SyncMutationResult? rejected = null;
        if (request.ExpectedRevision == 0 ? current is not null :
            current is null || current.Tombstone || current.Revision != request.ExpectedRevision)
            rejected = new(409, "revision_conflict", metadata, current);
        else if (CreationChanged(current?.Value, request.Value))
            rejected = new(409, "creation_metadata_immutable", metadata, current);
        else if (request.EntityType == "completion" && request.Action == "upsert" &&
            await RecordAsync("homework", request.EntityId) is not { Tombstone: false })
            // Frozen contract has no missing_homework code; conflict's record is the requested completion.
            rejected = new(409, "revision_conflict", metadata, current);
        else if (request.EntityType == "friend" && current is null && await ScalarAsync<long>(
            $"SELECT count(*) FROM {Schema}.sync_records WHERE user_id=@p0 AND entity_type='friend' AND NOT tombstone", UserId) >= 5)
            rejected = new(409, "friend_limit", metadata, null);

        if (rejected is not null) return await SaveReceiptAsync(request.OpId, digest, rejected);
        var written = await WriteAsync(metadata, request.EntityType, request.EntityId, request.ExpectedRevision,
            request.Action == "delete", request.Value, request.OpId);
        metadata = written.Metadata;
        if (request.EntityType == "homework" && request.Action == "delete" &&
            await RecordAsync("completion", request.EntityId) is { Tombstone: false } completion)
        {
            var cascade = await WriteAsync(metadata, "completion", request.EntityId, completion.Revision, true, null, request.OpId);
            metadata = cascade.Metadata;
        }
        return await SaveReceiptAsync(request.OpId, digest, new(200, "applied", metadata, written.Record));
    }

    private static bool CreationChanged(SyncValue? before, SyncValue? after) => (before, after) switch
    {
        (HomeworkValue a, HomeworkValue b) => a.CreatedAtUtc != b.CreatedAtUtc || a.LegacyCreatedLocalDate != b.LegacyCreatedLocalDate,
        (OverrideValue a, OverrideValue b) => a.CreatedAtUtc != b.CreatedAtUtc,
        _ => false
    };

    private async Task<Receipt?> ReceiptAsync(Guid opId)
    {
        await using var command = Command($"SELECT request_digest,status,body FROM {Schema}.sync_receipts WHERE user_id=@p0 AND op_id=@p1", UserId, opId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var body = reader.GetFieldValue<byte[]>(2);
        if (SyncJson.Parse<SyncMutationResult>(body).Status != reader.GetInt32(1)) throw new InvalidOperationException("Нарушена квитанция Sync.");
        return new(reader.GetFieldValue<byte[]>(0), body);
    }

    private async Task<SyncMutationResult> SaveReceiptAsync(Guid opId, byte[] digest, SyncMutationResult result)
    {
        await ExecuteAsync($"""
            INSERT INTO {Schema}.sync_receipts(user_id,op_id,request_digest,status,body,created_at)
            VALUES(@p0,@p1,@p2,@p3,@p4,@p5)
            """, UserId, opId, digest, result.Status, SyncJson.Serialize(result), Now);
        return result;
    }

    private sealed record Receipt(byte[] Digest, byte[] Body)
    {
        public override string ToString() => "Receipt { [REDACTED] }";
    }
}
