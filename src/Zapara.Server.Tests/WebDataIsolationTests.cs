using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Communities;
using Zapara.Contracts.Sync;
using Zapara.Server.Accounts;
using Zapara.Server.Communities;
using Zapara.Server.Sync;

namespace Zapara.Server.Tests;

public sealed class WebDataIsolationTests
{
    [Fact]
    public async Task BrowserMutationIsVisibleToNativeAccountButNotAnotherBrowserAccount()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var settings = new Dictionary<string, string?> { ["Sync:Enabled"] = "true", ["Sync:Schema"] = db.Schema };
        await using var first = new WebAccountHost(db.Accounts, moduleSettings: settings);
        await first.Bootstrap();
        await first.Send("POST", "/auth/register", 201, new RegisterRequest("sync_browser_a", WebAccountHost.Password));
        await first.Send("POST", "/auth/register", 201, new RegisterRequest("sync_browser_b", WebAccountHost.Password));
        await first.Login("sync_browser_a");
        var metadata = await first.Send("GET", "/sync/metadata", 200);
        var id = Guid.NewGuid();
        var epoch = metadata.GetProperty("syncEpoch").GetGuid();
        var mutation = new SyncMutation(epoch, Guid.NewGuid(), "homework", id, 0, "upsert",
            new HomeworkValue("Математика", "математика", "Из браузера", 2, new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero), new(2026, 9, 21)));
        await first.Send("POST", "/sync/mutations", 200, mutation);
        var accounts = first.Factory.Services.GetRequiredService<AccountService>();
        var native = await accounts.LoginAsync(new("sync_browser_a", WebAccountHost.Password, new(Guid.NewGuid(), "Android", "android")), Ct);
        var sync = first.Factory.Services.GetRequiredService<SyncService>();
        Assert.Equal(1, (await sync.MetadataAsync(native.AccessToken, Ct)).CurrentSequence);
        var read = await sync.ChangesAsync(native.AccessToken, epoch, 0, 20, Ct);
        Assert.Null(read.Error);
        var manifest = await first.Send("POST", "/sync/resync", 200);
        Assert.True(manifest.TryGetProperty("manifestId", out _));
        await using var second = new WebAccountHost(db.Accounts, moduleSettings: settings);
        await second.Bootstrap();
        await second.Login("sync_browser_b");
        var empty = await second.Send("GET", "/sync/metadata", 200);
        Assert.Equal(0, empty.GetProperty("currentSequence").GetInt64());
        await second.Send("GET", "/sync/metadata", 409, family: first.Family);
    }

    [Fact]
    public async Task BrowserCommunityPublicationUsesLiveNativeRoleChecks()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await db.Accounts.Migrations.EnsureAsync(Ct);
        await using var host = new WebAccountHost(db.Accounts, moduleSettings: new()
        {
            ["Communities:Enabled"] = "true", ["Communities:Schema"] = db.Schema
        });
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("community_browser", WebAccountHost.Password));
        var login = await host.Login("community_browser");
        var user = login.GetProperty("user").GetProperty("userId").GetGuid();
        var community = Guid.NewGuid();
        await db.SeedCommunityAsync(community);
        await db.SeedStaffAsync(community, user);
        await host.Send("POST", $"/communities/{community}/homework", 201, new HomeworkUpsert("Задание", "Из браузера", 0));
        var accounts = host.Factory.Services.GetRequiredService<AccountService>();
        var native = await accounts.LoginAsync(new("community_browser", WebAccountHost.Password, new(Guid.NewGuid(), "Windows", "windows")), Ct);
        var service = host.Factory.Services.GetRequiredService<CommunityService>();
        Assert.Equal("Из браузера", Assert.Single(await service.ListHomeworkAsync(native.AccessToken, community, Ct)).Body);
        await db.RevokeStaffAsync(community, user);
        await host.Send("POST", $"/communities/{community}/homework", 403, new HomeworkUpsert("Не публиковать", "Отозванная роль", 0));
        Assert.Single(await service.ListHomeworkAsync(native.AccessToken, community, Ct));
    }
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
