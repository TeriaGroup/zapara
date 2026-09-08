using System.Net;
using Xunit;
using Zapara.Contracts.Sync;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class SyncApiTests
{
    [Fact]
    public async Task Materialized_pages_and_feed_are_pinned_and_owner_scoped()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        await using var host = new SyncApiTestHost(db);
        var a = (await Seed(host.Accounts, "user.a")).AccessToken;
        var b = (await Seed(host.Accounts, "user.b")).AccessToken;
        var metadata = await host.Get<SyncMetadata>("/metadata", a);
        var put = Put(metadata);
        await host.Mutate(put, a);
        await host.Mutate(Put(metadata), a);
        var manifest = SyncJson.Parse<SyncResyncManifest>(await host.Send("POST", "/resync", 200, a));
        var page = await host.Send("GET", $"/resync/{manifest.ManifestId}?limit=1", 200, a);
        await host.Mutate(Put(metadata, put.EntityId, 1, text: "Изменено"), a);
        Assert.Equal(page, await host.Send("GET", $"/resync/{manifest.ManifestId}?limit=1", 200, a));
        var first = SyncJson.Parse<SyncResyncPage>(page);
        Assert.True(first.HasMore);
        var last = await host.Get<SyncResyncPage>($"/resync/{manifest.ManifestId}?afterOrdinal={first.NextAfterOrdinal}&limit=1", a);
        Assert.False(last.HasMore);
        Assert.Equal(2, last.NextAfterOrdinal);
        var feed = await host.Get<SyncChangesPage>($"/changes?epoch={metadata.SyncEpoch}&limit=1", a);
        Assert.Equal(1, feed.NextAfterSequence);
        Assert.True(feed.HasMore);
        Assert.Equal(3, feed.Metadata.CurrentSequence);
        var after = await host.Get<SyncChangesPage>($"/changes?epoch={metadata.SyncEpoch}&afterSequence={manifest.HighWater}", a);
        Assert.Single(after.Changes);
        Assert.Equal(3, after.NextAfterSequence);
        var foreign = SyncJson.Parse<SyncError>(await host.Send("GET", $"/resync/{manifest.ManifestId}", 410, b));
        Assert.Equal("manifest_expired", foreign.Code);
        var bm = await host.Get<SyncMetadata>("/metadata", b);
        Assert.Empty((await host.Get<SyncChangesPage>($"/changes?epoch={bm.SyncEpoch}", b)).Changes);
        Assert.Equal("sync_reset", SyncJson.Parse<SyncError>(await host.Send("GET", $"/changes?epoch={metadata.SyncEpoch}", 410, b)).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retention_410_commits_rotation_before_response(bool mutation)
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var clock = new AccountClock();
        await using var host = new SyncApiTestHost(db, clock);
        var a = (await Seed(host.Accounts)).AccessToken;
        var metadata = await host.Get<SyncMetadata>("/metadata", a);
        var put = Put(metadata);
        await host.Mutate(put, a);
        var manifest = SyncJson.Parse<SyncResyncManifest>(await host.Send("POST", "/resync", 200, a));
        clock.Now += TimeSpan.FromDays(91);
        a = (await host.Accounts.LoginAsync(Login(), SyncApiTestHost.Ct)).AccessToken;
        if (mutation) Assert.Equal("sync_reset", SyncJson.Parse<SyncMutationResult>(await host.Mutate(put, a, 410)).Code);
        else Assert.Equal("sync_reset", SyncJson.Parse<SyncError>(await host.Send("GET", $"/changes?epoch={metadata.SyncEpoch}", 410, a)).Code);
        var current = await host.Get<SyncMetadata>("/metadata", a);
        Assert.NotEqual(metadata.SyncEpoch, current.SyncEpoch);
        Assert.Equal(1, current.MinAfterSequence);
        Assert.Equal(1, current.CurrentSequence);
        Assert.Equal(0, await Count(db, "sync_receipts"));
        Assert.Equal(0, await Count(db, "sync_changes"));
        Assert.Equal(0, await Count(db, "sync_manifests"));
        await host.Send("GET", $"/changes?epoch={current.SyncEpoch}&afterSequence=0", 410, a);
        await host.Send("GET", $"/resync/{manifest.ManifestId}", 410, a);
    }

    [Fact]
    public async Task Expired_manifest_and_revoked_access_never_return_private_success()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var clock = new AccountClock();
        await using var host = new SyncApiTestHost(db, clock, observe: true);
        var a = (await Seed(host.Accounts)).AccessToken;
        var metadata = await host.Get<SyncMetadata>("/metadata", a);
        var put = Put(metadata);
        await host.Mutate(put, a);
        var manifest = SyncJson.Parse<SyncResyncManifest>(await host.Send("POST", "/resync", 200, a));
        clock.Now += TimeSpan.FromMinutes(10);
        await host.Send("GET", $"/resync/{manifest.ManifestId}", 410, a);
        await host.Accounts.LogoutAsync(a, SyncApiTestHost.Ct);
        var callbacks = host.Callbacks;
        await host.Send("GET", "/metadata", 401, a);
        await host.Mutate(put, a, 401);
        await host.Mutate(Put(metadata), a, 401);
        await host.Send("POST", "/resync", 401, a);
        await host.Send("GET", $"/resync/{manifest.ManifestId}", 401, a);
        await host.Send("GET", $"/changes?epoch={metadata.SyncEpoch}", 401, a);
        Assert.Equal(callbacks, host.Callbacks);
        Assert.Equal(1, await Count(db, "sync_receipts"));
        Assert.Equal(1, await Count(db, "sync_changes"));
    }

    [Fact]
    public async Task Invalid_sync_configuration_and_missing_storage_are_503_not_auth_failures()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        await using (var host = new SyncApiTestHost(db, overrides: new() { ["Sync:Schema"] = db.Accounts.Schema }))
        {
            var a = (await Seed(host.Accounts)).AccessToken;
            await host.Send("GET", "/metadata", 503, a);
            Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/health/live", SyncApiTestHost.Ct)).StatusCode);
        }
        await using (var host = new SyncApiTestHost(db))
        {
            var a = (await host.Accounts.LoginAsync(Login(), SyncApiTestHost.Ct)).AccessToken;
            await db.Accounts.ExecuteAsync($"DROP TABLE {db.QuotedSchema}.sync_state CASCADE");
            await host.Send("GET", "/metadata", 503, a);
        }
        await using (var host = new SyncApiTestHost(db, overrides: new()
        { ["ConnectionStrings:Accounts"] = "Host=127.0.0.1;Port=1;Database=unavailable;Username=synthetic;Timeout=1" }))
            await host.Send("GET", "/metadata", 503, "za_" + new string('A', 43));
    }

    [Fact]
    public async Task Disabled_sync_is_absent_and_rate_limit_is_bounded()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        await using (var host = new SyncApiTestHost(db, overrides: new() { ["Sync:Enabled"] = "false" }))
            Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/v1/sync/metadata", SyncApiTestHost.Ct)).StatusCode);
        await using (var host = new SyncApiTestHost(db))
        {
            for (var i = 0; i < 120; i++) await host.Send("GET", "/metadata", 401);
            await host.Send("GET", "/metadata", 429);
            Assert.Equal(0, await Count(db, "sync_state"));
            Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/health/live", SyncApiTestHost.Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PostAsync("/api/v1/ingest", null, SyncApiTestHost.Ct)).StatusCode);
        }
    }
}
