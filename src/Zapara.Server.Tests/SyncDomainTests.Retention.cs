using Xunit;
using Zapara.Contracts.Sync;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class SyncDomainTests
{
    [Fact]
    public async Task Deleted_UUID_cannot_be_recreated_and_pruning_commits_epoch_before_old_retry()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var clock = new AccountClock();
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, clock);
        var session = await Seed(accounts);
        var service = new SyncService(accounts, db.Configuration);
        var m = await service.MetadataAsync(session.AccessToken, Ct);
        Assert.NotNull(m);
        var original = Put(m, Guid.NewGuid());
        var live = await service.MutateAsync(session.AccessToken, original, Ct);
        var retained = await service.MutateAsync(session.AccessToken, Put(m, Guid.NewGuid()), Ct);
        var deleted = await service.MutateAsync(session.AccessToken, Delete(m, live.ServerRecord!), Ct);
        Assert.True(deleted.ServerRecord!.Tombstone);
        var stale = await service.MutateAsync(session.AccessToken, Put(m, original.EntityId), Ct);
        Assert.Equal(409, stale.Status);
        Assert.Equal(deleted.ServerRecord, stale.ServerRecord);
        var manifest = await service.BeginResyncAsync(session.AccessToken, Ct);
        clock.Now = clock.Now.AddDays(91);
        session = await accounts.LoginAsync(Login(), Ct); // Absolute family expiry is 30 days.
        var reset = await service.MutateAsync(session.AccessToken, original, Ct);
        Assert.Equal(410, reset.Status);
        Assert.Equal("sync_reset", reset.Code); // Frozen wire spelling; not silently renamed.
        var fresh = await service.MetadataAsync(session.AccessToken, Ct);
        Assert.NotEqual(m.SyncEpoch, fresh.SyncEpoch);
        Assert.Equal(deleted.Metadata.CurrentSequence, fresh.CurrentSequence);
        Assert.Equal(fresh.CurrentSequence, fresh.MinAfterSequence);
        Assert.Equal(0, await Count(db, "sync_receipts"));
        Assert.Equal(0, await Count(db, "sync_changes"));
        Assert.Equal(0, await Count(db, "sync_manifests"));
        Assert.Equal(0, await Count(db, "sync_manifest_items"));
        var snapshot = await service.BeginResyncAsync(session.AccessToken, Ct);
        Assert.Equal(retained.ServerRecord, Assert.Single(Value(await service.ReadResyncPageAsync(session.AccessToken, snapshot.ManifestId, 0, 200, Ct)).Items).Record);
        Error(await service.ChangesAsync(session.AccessToken, m.SyncEpoch, 0, 100, Ct), 410, "sync_reset");
        Error(await service.ChangesAsync(session.AccessToken, fresh.SyncEpoch, 0, 100, Ct), 410, "sync_reset");
        Error(await service.ReadResyncPageAsync(session.AccessToken, manifest.ManifestId, 0, 100, Ct), 410, "manifest_expired");
        TestContext.Current.TestOutputHelper!.WriteLine("SQL pruning: receipts/feed/old manifests/items=0; live revision/counter preserved; stale epoch reset committed");
    }

    [Fact]
    public async Task Maintenance_is_daily_strictly_older_than_90_days_and_rotates_only_on_prune()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var clock = new AccountClock();
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, clock);
        var session = await Seed(accounts);
        var service = new SyncService(accounts, db.Configuration);
        var m = await service.MetadataAsync(session.AccessToken, Ct);
        Assert.NotNull(m);
        await service.MutateAsync(session.AccessToken, Put(m, Guid.NewGuid()), Ct);
        clock.Now = clock.Now.AddDays(90);
        session = await accounts.LoginAsync(Login(), Ct);
        Assert.Equal(m.SyncEpoch, (await service.MetadataAsync(session.AccessToken, Ct)).SyncEpoch);
        Assert.Equal(1, await Count(db, "sync_receipts"));
        var last = await db.Accounts.ScalarAsync<DateTime>($"SELECT last_maintenance_at FROM {db.QuotedSchema}.sync_state");
        clock.Now = clock.Now.AddHours(23);
        session = await accounts.LoginAsync(Login(), Ct);
        Assert.Equal(m.SyncEpoch, (await service.MetadataAsync(session.AccessToken, Ct)).SyncEpoch);
        Assert.Equal(last, await db.Accounts.ScalarAsync<DateTime>($"SELECT last_maintenance_at FROM {db.QuotedSchema}.sync_state"));
        clock.Now = clock.Now.AddHours(1);
        session = await accounts.LoginAsync(Login(), Ct);
        var fresh = await service.MetadataAsync(session.AccessToken, Ct);
        Assert.NotEqual(m.SyncEpoch, fresh.SyncEpoch);
        clock.Now = clock.Now.AddDays(1);
        session = await accounts.LoginAsync(Login(), Ct);
        Assert.Equal(fresh.SyncEpoch, (await service.MetadataAsync(session.AccessToken, Ct)).SyncEpoch);
    }
}
