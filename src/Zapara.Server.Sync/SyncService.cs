using Zapara.Contracts.Sync;
using Zapara.Server.Accounts;

namespace Zapara.Server.Sync;

public sealed partial class SyncService(IAccountUnitOfWork trustedAccounts, SyncConfiguration configuration)
{
    public Task<SyncMetadata> MetadataAsync(string bearer, CancellationToken ct = default)
        => trustedAccounts.ExecuteAsync(bearer, (context, token) => new SyncRepository(context, configuration, token).InitializeAsync(), ct);
    public Task<SyncMutationResult> MutateAsync(string bearer, SyncMutation request, CancellationToken ct = default)
        => trustedAccounts.ExecuteAsync(bearer, async (context, token) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var db = new SyncRepository(context, configuration, token);
            var metadata = await db.InitializeAsync();
            return await db.MutateAsync(metadata, request);
        }, ct);
    public Task<SyncReadResult<SyncChangesPage>> ChangesAsync(string bearer, Guid epoch, long afterSequence,
        int limit = 100, CancellationToken ct = default)
        => trustedAccounts.ExecuteAsync(bearer, async (context, token) =>
        {
            var db = new SyncRepository(context, configuration, token);
            return await db.ChangesAsync(await db.InitializeAsync(), epoch, afterSequence, limit);
        }, ct);
    public Task<SyncResyncManifest> BeginResyncAsync(string bearer, CancellationToken ct = default)
        => trustedAccounts.ExecuteAsync(bearer, async (context, token) =>
        {
            var db = new SyncRepository(context, configuration, token);
            return await db.BeginResyncAsync(await db.InitializeAsync());
        }, ct);
    public Task<SyncReadResult<SyncResyncPage>> ReadResyncPageAsync(string bearer, Guid manifestId, long afterOrdinal,
        int limit = 100, CancellationToken ct = default)
        => trustedAccounts.ExecuteAsync(bearer, async (context, token) =>
        {
            var db = new SyncRepository(context, configuration, token);
            return await db.ReadResyncPageAsync(await db.InitializeAsync(), manifestId, afterOrdinal, limit);
        }, ct);
}
