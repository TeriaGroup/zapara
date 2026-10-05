using System.Net;
using System.Net.Http.Json;
using Zapara.Contracts.Accounts;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserStateSessionTests
{
    [Fact]
    public async Task Guest_does_not_inherit_previous_accounts_sync_status()
    {
        using var server = new SessionServer();
        using var http = new HttpClient(server) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser());
        var api = new BrowserApiClient(http, storage);
        var state = new WebAppState(http, storage, api);
        await state.InitializeAsync();
        await state.SynchronizeAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(state.SyncMessage);

        server.Authenticated = false;
        await api.RefreshSessionAsync(TestContext.Current.CancellationToken);

        Assert.True(state.IsGuest);
        Assert.Null(state.SyncMessage);
    }

    [Fact]
    public async Task New_session_of_same_account_invalidates_existing_editor_generation()
    {
        using var server = new SessionServer();
        using var http = new HttpClient(server) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser());
        var api = new BrowserApiClient(http, storage);
        var state = new WebAppState(http, storage, api);
        await state.InitializeAsync();
        var owner = state.ProfileKey;
        var generation = state.Generation;

        server.Family = Guid.NewGuid();
        await api.RefreshSessionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(owner, state.ProfileKey);
        Assert.True(state.Generation > generation);
    }

    private sealed class SessionServer : HttpMessageHandler
    {
        public bool Authenticated { get; set; } = true;
        public Guid Family { get; set; } = Guid.NewGuid();
        private readonly Guid user = Guid.NewGuid();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath == "/web-api/session"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { authenticated = Authenticated,
                    user = Authenticated ? new { userId = user, username = "student", displayName = "Студент", createdAt = "2026-09-21T00:00:00Z" } : null,
                    familyId = Authenticated ? Family : (Guid?)null, csrfToken = new string('a', 43), capabilities = new { password = true, vk = false, yandex = false, registration = true, recovery = false } }) }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
