using System.Net;
using System.Net.Http.Headers;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Sync;
using Vograph.Desktop;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Profiles;
using Vograph.Desktop.Shell;
using Xunit;
using Zapara.Contracts.Sync;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class PrivateSyncOutboxAttachTests
{
    [Fact]
    public void Guest_start_does_not_attach_sync_http_and_may_start_lan()
    {
        using var dir = new ProfileTestDirectory();
        using var guest = AppServices.Create(dir.Root, () => false);
        guest.AllowNetwork = false;
        guest.Prefs.LanSync = true;
        guest.LanSync.Dispose();
        guest.LanSync = new LanSyncServer(guest, 0, localhostOnly: true);
        var root = new ProfileRoot(guest, new ShellViewModel(guest));
        using var accounts = new AccountHttpClient(new HttpClient(new Silent()), new Uri("http://127.0.0.1/outbox-test/"));
        using var vault = new AccountMemoryVault(accounts.Scope.Key);
        var sessions = new AccountSessionManager(accounts, vault);

        App.StartCurrentProfile(root, accounts, sessions, _ => throw new InvalidOperationException("guest must not create a sync HTTP client"));

        Assert.Null(guest.PrivateSync);
        Assert.True(guest.LanSync.IsRunning);
        Assert.True(guest.Profile.IsGuest);
    }

    [Fact]
    public async Task Account_start_attaches_same_scope_and_does_not_start_lan()
    {
        var syncCalls = 0;
        Uri? syncBase = null;
        string? bearer = null;
        await using var h = new ProfileHarness();
        Assert.True((await h.Coordinator.LoginAsync("Test.User", Password, TestContext.Current.CancellationToken)).Committed);
        var account = h.Coordinator.Current.Services;
        account.Prefs.LanSync = true;
        account.AllowNetwork = false;
        var sessions = new AccountSessionManager(h.Client, h.Vault, new AccountClientClock());
        App.StartCurrentProfile(h.Coordinator.Current, h.Client, sessions, uri =>
        {
            syncBase = uri;
            var http = new HttpClient(new Script((request, _) =>
            {
                Interlocked.Increment(ref syncCalls);
                bearer = request.Headers.Authorization?.Parameter;
                var error = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new ByteArrayContent(SyncJson.Serialize(new SyncError(503, "db_unavailable")))
                };
                error.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return error;
            }));
            return new PrivateSyncHttpClient(http, uri);
        });

        Assert.NotNull(account.PrivateSync);
        Assert.True(account.PrivateSync!.IsAttached);
        Assert.True(account.PrivateSync.IsBackgroundRunning);
        Assert.Equal(h.Client.Scope.BaseUri, account.PrivateSync.AttachedBaseUri);
        Assert.Equal(h.Client.Scope.BaseUri, syncBase);
        Assert.False(account.LanSync.IsRunning);
        Assert.Throws<InvalidOperationException>(() => account.LanSync.Start());
        await Waits.Until(() => syncCalls > 0 && bearer is not null, "sync worker used the vault access token", 5000);
        Assert.Equal(((AccountMemoryVault)h.Vault).Entry!.Session.AccessToken, bearer);
        Assert.StartsWith("za_", bearer, StringComparison.Ordinal);
    }

    private sealed class Silent : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class Script(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(action(request, cancellationToken));
    }
}
