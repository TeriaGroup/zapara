using Xunit;
using Zapara.Contracts.Sync;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class SyncDomainTests
{
    [Fact]
    public async Task Manifest_is_immutable_reused_without_extension_and_pages_cover_snapshot_then_feed_after_H()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var clock = new AccountClock();
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, clock);
        var session = await Seed(accounts);
        var service = new SyncService(accounts, db.Configuration);
        var m = await service.MetadataAsync(session.AccessToken, Ct);
        Assert.NotNull(m);
        var records = new List<SyncRecord>();
        for (var i = 0; i < 205; i++) records.Add((await service.MutateAsync(session.AccessToken, Put(m, Guid.NewGuid(), text: new string('Ж', 4000)), Ct)).ServerRecord!);
        records[0] = (await service.MutateAsync(session.AccessToken, Delete(m, records[0]), Ct)).ServerRecord!;
        var manifest = await service.BeginResyncAsync(session.AccessToken, Ct);
        Assert.NotNull(manifest);
        Assert.Equal(205, manifest.ItemCount);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.Equal(manifest, await service.BeginResyncAsync(session.AccessToken, Ct));
        var change = await service.MutateAsync(session.AccessToken, Put(m, records[1].EntityId, records[1].Revision, "Новое"), Ct);
        var seen = new List<SyncManifestItem>();
        long after = 0;
        while (true)
        {
            var page = Value(await service.ReadResyncPageAsync(session.AccessToken, manifest.ManifestId, after, 200, Ct));
            Assert.InRange(page.Items.Count, 1, 200);
            Assert.InRange(SyncJson.Serialize(page).Length, 1, SyncValidation.PageBytes);
            seen.AddRange(page.Items);
            after = page.NextAfterOrdinal;
            if (!page.HasMore) break;
        }
        Assert.Equal(records.OrderBy(x => x.EntityId), seen.Select(x => x.Record).OrderBy(x => x.EntityId));
        Assert.Equal(Enumerable.Range(1, 205).Select(x => (long)x), seen.Select(x => x.Ordinal));
        var pull = Value(await service.ChangesAsync(session.AccessToken, m.SyncEpoch, manifest.HighWater, 200, Ct));
        Assert.Equal(change.ServerRecord, Assert.Single(pull.Changes).Record);
        var first = Value(await service.ChangesAsync(session.AccessToken, m.SyncEpoch, 0, 2, Ct));
        Assert.True(first.HasMore);
        Assert.Equal(2, first.NextAfterSequence);
        Assert.True(first.NextAfterSequence < first.Metadata.CurrentSequence);
        Assert.Equal(records[1].Value, first.Changes[1].Record.Value);
        Error(await service.ChangesAsync(session.AccessToken, m.SyncEpoch, long.MaxValue, 100, Ct), 400, "invalid_cursor");
        Error(await service.ChangesAsync(session.AccessToken, Guid.Empty, 0, 100, Ct), 400, "invalid_cursor");
        Error(await service.ChangesAsync(session.AccessToken, m.SyncEpoch, -1, 100, Ct), 400, "invalid_cursor");
        Error(await service.ChangesAsync(session.AccessToken, m.SyncEpoch, 0, 201, Ct), 400, "invalid_request");
        Error(await service.ReadResyncPageAsync(session.AccessToken, manifest.ManifestId, 206, 100, Ct), 400, "invalid_cursor");
        Error(await service.ReadResyncPageAsync(session.AccessToken, Guid.Empty, 0, 100, Ct), 400, "invalid_cursor");
        Error(await service.ReadResyncPageAsync(session.AccessToken, manifest.ManifestId, -1, 100, Ct), 400, "invalid_cursor");
        Assert.Empty(Value(await service.ReadResyncPageAsync(session.AccessToken, manifest.ManifestId, 205, 100, Ct)).Items);
        clock.Now = manifest.ExpiresAt;
        Error(await service.ReadResyncPageAsync(session.AccessToken, manifest.ManifestId, 0, 100, Ct), 410, "manifest_expired");
        Assert.NotEqual(manifest.ManifestId, (await service.BeginResyncAsync(session.AccessToken, Ct)).ManifestId);
        Assert.Equal(1, await Count(db, "sync_manifests"));
    }

    [Fact]
    public async Task Foreign_epoch_and_manifest_never_expose_owner_content()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var a = await Seed(accounts, "owner.a");
        var b = await Seed(accounts, "owner.b");
        var service = new SyncService(accounts, db.Configuration);
        var m = await service.MetadataAsync(a.AccessToken, Ct);
        Assert.NotNull(m);
        await service.MutateAsync(a.AccessToken, Put(m, Guid.NewGuid()), Ct);
        var manifest = await service.BeginResyncAsync(a.AccessToken, Ct);
        Error(await service.ChangesAsync(b.AccessToken, m.SyncEpoch, 0, 100, Ct), 410, "sync_reset");
        Error(await service.ReadResyncPageAsync(b.AccessToken, manifest.ManifestId, 0, 100, Ct), 410, "manifest_expired");
        Assert.Equal(0, (await service.BeginResyncAsync(b.AccessToken, Ct)).ItemCount);
        Assert.Single(Value(await service.ReadResyncPageAsync(a.AccessToken, manifest.ManifestId, 0, 100, Ct)).Items);
    }
}
