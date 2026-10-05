using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zapara.Server.Accounts.ExternalProviders;
using Zapara.Server.Social;

namespace Zapara.Server.Tests;

[Collection("Account runtime")]
public sealed class MessagingInitializationTests
{
    [Fact]
    public async Task Social_initialization_retries_when_account_storage_is_prepared_after_startup()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, initialize: false);
        var clock = new ExternalProviderTestsClock();
        await using var host = new WebAccountHost(db, clock);
        var social = host.Factory.Services.GetRequiredService<SocialConfiguration>().Schema;
        try
        {
            Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{social}'"));
            await db.Migrations.EnsureAsync(TestContext.Current.CancellationToken);
            clock.Advance(TimeSpan.FromSeconds(11));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            var ready = false;
            while (!deadline.IsCancellationRequested)
            {
                ready = await db.ScalarAsync<bool>($"SELECT to_regclass('{social}.messages') IS NOT NULL");
                if (ready) break;
                try { await Task.Delay(20, deadline.Token); }
                catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested) { break; }
            }
            Assert.True(ready, "Chat initialization must retry after account storage becomes available.");
        }
        finally
        {
            await host.DisposeAsync();
            await db.ExecuteAsync($"DROP SCHEMA IF EXISTS \"{social}\" CASCADE");
        }
    }

    [Fact]
    public async Task Platform_readiness_reports_enabled_unprepared_chats_as_missing()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, initialize: false);
        await using var host = new WebAccountHost(db);
        using var response = await host.Client.GetAsync("/health/platform", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("missing", body.RootElement.GetProperty("modules").GetProperty("social").GetString());
        Assert.Equal("disabled", body.RootElement.GetProperty("modules").GetProperty("messenger").GetString());
    }

    [Fact]
    public async Task Messenger_initialization_retries_when_communities_are_prepared_after_startup()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(initialize: false);
        var clock = new ExternalProviderTestsClock();
        await using var host = new WebAccountHost(db.Accounts, clock, moduleSettings: new()
        { ["Communities:Enabled"] = "true", ["Communities:Schema"] = db.Schema });
        var messages = db.Configuration.MessagesSchema;
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{messages}'"));
        await db.Migrations.EnsureAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(11));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var ready = false;
        while (!deadline.IsCancellationRequested)
        {
            ready = await db.Accounts.ScalarAsync<bool>($"SELECT to_regclass('{messages}.group_space_state') IS NOT NULL");
            if (ready) break;
            try { await Task.Delay(20, deadline.Token); }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested) { break; }
        }
        Assert.True(ready, "Messenger initialization must retry without waiting for a system-ballot interval.");
    }

    [Fact]
    public async Task Stopping_the_host_cancels_pending_initialization_retries()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, initialize: false);
        var clock = new ExternalProviderTestsClock();
        await using (var host = new WebAccountHost(db, clock))
            Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{db.Schema}_social'"));
        Assert.Equal(0, clock.LiveTimers);
        await db.Migrations.EnsureAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{db.Schema}_social'"));
    }
}
