using System.Net;
using System.Text.Json;
using Microsoft.JSInterop;
using Xunit;
using Zapara.Web.Services;

namespace Zapara.Web.Tests;

public sealed class BrowserSessionCoordinationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stable_offline_cold_start_does_not_claim_that_an_account_transition_happened()
    {
        var shared = new Shared(); using var http = Client(_ => throw new HttpRequestException());
        await using var storage = new BrowserStorage(new Tab(shared)); await using var api = new BrowserApiClient(http, storage);
        await api.InitializeBrowserCoordinationAsync();
        await Assert.ThrowsAsync<BrowserApiException>(() => api.RefreshSessionAsync(Ct));
        Assert.False(api.Transitioning); Assert.False(api.Available);
        Assert.Null(api.ConfirmedSessionGeneration); Assert.Equal("initial", api.ObservedSessionGeneration);
    }

    [Fact]
    public async Task OAuth_return_bootstraps_but_remains_masked_until_terminal_result_completion()
    {
        var shared = new Shared(); var tab = new Tab(shared);
        shared.Marker = new(Guid.NewGuid().ToString(), "redirect", tab.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        using var http = Client(_ => Task.FromResult(BrowserApiClientTests.Session(shared.Family)));
        await using var storage = new BrowserStorage(tab); await using var api = new BrowserApiClient(http, storage);
        await api.InitializeBrowserCoordinationAsync();
        Assert.True(api.ExternalTransitionPending); Assert.True(api.Transitioning);
        Assert.Equal("redirect", shared.Marker.Phase); Assert.Null(shared.LockedBy);
        shared.Family = Guid.NewGuid();
        await api.CompleteExternalTransitionAsync(Ct);
        Assert.False(api.ExternalTransitionPending); Assert.False(api.Transitioning);
        Assert.Equal("stable", shared.Marker.Phase); Assert.Equal(shared.Family, api.Session.FamilyId);
    }

    [Fact]
    public async Task A_new_authoritative_generation_releases_stale_local_transition_ownership()
    {
        var shared = new Shared(); using var http = Client(_ => Task.FromResult(BrowserApiClientTests.Session(shared.Family)));
        await using var storage = new BrowserStorage(new Tab(shared)); await using var api = new BrowserApiClient(http, storage);
        await api.InitializeBrowserCoordinationAsync(); await api.RefreshSessionAsync(Ct);
        await api.BeginExternalTransitionAsync(Ct); await api.RefreshSessionAsync(Ct);
        shared.Marker = new(Guid.NewGuid().ToString(), "stable", "another-tab", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await api.RefreshSessionAsync(Ct);
        await api.SignInAsync("student", "password", ct: Ct);
        Assert.False(api.Transitioning); Assert.Equal("stable", shared.Marker.Phase);
    }

    [Fact]
    public async Task Failed_login_with_unchanged_identity_still_publishes_the_new_confirmed_owner_stamp()
    {
        var shared = new Shared();
        using var http = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/login"))
            {
                var error = BrowserApiClientTests.Json("{\"code\":\"invalid_credentials\"}"); error.StatusCode = HttpStatusCode.Unauthorized;
                return Task.FromResult(error);
            }
            return Task.FromResult(BrowserApiClientTests.Session(shared.Family));
        });
        await using var storage = new BrowserStorage(new Tab(shared)); await using var api = new BrowserApiClient(http, storage);
        await api.InitializeBrowserCoordinationAsync(); await api.RefreshSessionAsync(Ct);
        var stamps = new List<string?>(); api.SessionChanged += _ => { stamps.Add(api.ConfirmedSessionGeneration); return Task.CompletedTask; };
        await Assert.ThrowsAsync<BrowserApiException>(() => api.SignInAsync("student", "wrong", ct: Ct));
        Assert.Equal(shared.Marker.Generation, Assert.Single(stamps)); Assert.False(api.Transitioning);
    }

    [Fact]
    public async Task Failed_profile_callback_is_retried_before_the_same_session_is_unmasked()
    {
        var shared = new Shared(); using var http = Client(_ => Task.FromResult(BrowserApiClientTests.Session(shared.Family)));
        await using var storage = new BrowserStorage(new Tab(shared)); await using var api = new BrowserApiClient(http, storage);
        await api.InitializeBrowserCoordinationAsync();
        var calls = 0;
        api.SessionChanged += _ => { if (++calls == 1) throw new JSException("QuotaExceededError"); return Task.CompletedTask; };
        await Assert.ThrowsAsync<JSException>(() => api.RefreshSessionAsync(Ct));
        await api.RefreshSessionAsync(Ct);
        Assert.Equal(2, calls); Assert.False(api.Transitioning);
    }

    [Fact]
    public async Task Failed_abandoned_transition_bootstrap_releases_lock_but_keeps_verification_required()
    {
        var shared = new Shared { Marker = new(Guid.NewGuid().ToString(), "transition", "closed-tab", 1) };
        using var http = Client(_ => throw new HttpRequestException());
        await using var storage = new BrowserStorage(new Tab(shared)); await using var api = new BrowserApiClient(http, storage);
        await api.InitializeBrowserCoordinationAsync();
        Assert.Null(shared.LockedBy);
        Assert.Equal("verify", shared.Marker.Phase);
        Assert.True(api.Transitioning);
    }

    [Fact]
    public async Task Marker_is_rechecked_after_a_slow_private_response_body()
    {
        var shared = new Shared(); var body = new SlowBody();
        using var httpA = Client(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/session")
            ? BrowserApiClientTests.Session(shared.Family) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
        using var httpB = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/login")) shared.Family = Guid.NewGuid();
            return Task.FromResult(BrowserApiClientTests.Session(shared.Family));
        });
        await using var sa = new BrowserStorage(new Tab(shared)); await using var sb = new BrowserStorage(new Tab(shared));
        await using var a = new BrowserApiClient(httpA, sa); await using var b = new BrowserApiClient(httpB, sb);
        await a.InitializeBrowserCoordinationAsync(); await b.InitializeBrowserCoordinationAsync();
        await a.RefreshSessionAsync(Ct); await b.RefreshSessionAsync(Ct);
        var reading = a.GetAsync<JsonElement>("account/me", Ct); await body.Started.Task.WaitAsync(Ct);
        await b.SignInAsync("other", "password", ct: Ct); body.Release.SetResult();
        Assert.Equal("account_changed", (await Assert.ThrowsAsync<BrowserApiException>(() => reading)).Code);
    }

    private sealed class SlowBody() : MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"private\":\"old\"}"))
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    [Fact]
    public async Task Another_tabs_login_marker_precedes_cookie_request_and_invalidates_a_private_response_without_an_event()
    {
        var shared = new Shared();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var httpA = Client(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/web-api/session") return BrowserApiClientTests.Session(shared.Family);
            started.SetResult(); await release.Task.WaitAsync(Ct); return BrowserApiClientTests.Json("{\"private\":\"old\"}");
        });
        using var httpB = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/web-api/auth/login")
            {
                Assert.Equal("transition", shared.Marker.Phase);
                shared.Family = Guid.NewGuid();
            }
            return Task.FromResult(BrowserApiClientTests.Session(shared.Family));
        });
        await using var storeA = new BrowserStorage(new Tab(shared)); await using var storeB = new BrowserStorage(new Tab(shared));
        await using var a = new BrowserApiClient(httpA, storeA); await using var b = new BrowserApiClient(httpB, storeB);
        await a.InitializeBrowserCoordinationAsync(); await b.InitializeBrowserCoordinationAsync();
        await a.RefreshSessionAsync(Ct); await b.RefreshSessionAsync(Ct);
        var previousRevision = a.Revision;
        var old = a.GetAsync<JsonElement>("account/me", Ct); await started.Task.WaitAsync(Ct);
        await b.SignInAsync("another", "password", ct: Ct);
        Assert.Equal(previousRevision, a.Revision); // No BroadcastChannel/storage event was delivered to this tab.
        release.SetResult();
        var error = await Assert.ThrowsAsync<BrowserApiException>(() => old);
        Assert.Equal("account_changed", error.Code);
        Assert.True(a.Transitioning);
    }

    [Fact]
    public async Task Late_bootstrap_from_a_previous_shared_generation_is_discarded()
    {
        var shared = new Shared(); var delay = false;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var httpA = Client(async _ =>
        {
            var captured = shared.Family;
            if (delay) { started.TrySetResult(); await release.Task.WaitAsync(Ct); }
            return BrowserApiClientTests.Session(captured);
        });
        using var httpB = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/web-api/auth/login") shared.Family = Guid.NewGuid();
            return Task.FromResult(BrowserApiClientTests.Session(shared.Family));
        });
        await using var sa = new BrowserStorage(new Tab(shared)); await using var sb = new BrowserStorage(new Tab(shared));
        await using var a = new BrowserApiClient(httpA, sa); await using var b = new BrowserApiClient(httpB, sb);
        await a.InitializeBrowserCoordinationAsync(); await b.InitializeBrowserCoordinationAsync();
        await a.RefreshSessionAsync(Ct); await b.RefreshSessionAsync(Ct);
        delay = true; var old = a.RefreshSessionAsync(Ct); await started.Task.WaitAsync(Ct);
        await b.SignInAsync("other", "password", ct: Ct); release.SetResult();
        Assert.Equal("account_changed", (await Assert.ThrowsAsync<BrowserApiException>(() => old)).Code);
        delay = false; await a.RefreshSessionAsync(Ct);
        Assert.Equal(shared.Family, a.Session.FamilyId); Assert.False(a.Transitioning);
    }

    [Fact]
    public async Task Session_callback_may_refresh_authoritatively_without_deadlocking_or_recursive_events()
    {
        var shared = new Shared(); using var http = Client(_ => Task.FromResult(BrowserApiClientTests.Session(shared.Family)));
        await using var storage = new BrowserStorage(new Tab(shared)); await using var api = new BrowserApiClient(http, storage);
        await api.InitializeBrowserCoordinationAsync();
        var calls = 0;
        api.SessionChanged += async _ => { calls++; await api.RefreshSessionAsync(Ct); };
        await api.RefreshSessionAsync(Ct).WaitAsync(TimeSpan.FromSeconds(3), Ct);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Rotated_csrf_token_does_not_reenter_identity_callbacks()
    {
        var shared = new Shared(); var reads = 0;
        using var http = Client(async _ =>
        {
            var response = BrowserApiClientTests.Session(shared.Family);
            var json = await response.Content.ReadAsStringAsync(Ct);
            response.Content = new StringContent(json.Replace(new string('a', 43), new string(++reads == 1 ? 'a' : 'b', 43)));
            return response;
        });
        await using var storage = new BrowserStorage(new Tab(shared)); await using var api = new BrowserApiClient(http, storage);
        await api.InitializeBrowserCoordinationAsync();
        var calls = 0; api.SessionChanged += async _ => { calls++; await api.RefreshSessionAsync(Ct); };
        await api.RefreshSessionAsync(Ct).WaitAsync(TimeSpan.FromSeconds(3), Ct);
        Assert.Equal(1, calls); Assert.Equal(new string('b', 43), api.Session.CsrfToken);
    }

    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) => new(new Handler(callback)) { BaseAddress = new("https://zapara.test/app/") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }
    private sealed class Shared
    {
        public Guid Family = Guid.NewGuid(); public SessionMarker Marker = SessionMarker.Initial; public string? LockedBy;
        public MemoryBrowser Disk = new();
    }
    private sealed class Tab(Shared shared) : IJSRuntime, IJSObjectReference
    {
        private readonly string id = Guid.NewGuid().ToString();
        public string Id => id;
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => InvokeAsync<T>(name, CancellationToken.None, args);
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken ct, object?[]? args)
        {
            ct.ThrowIfCancellationRequested();
            object? result;
            if (name == "import") result = ((string)args![0]!).Contains("session-coordination") ? this : shared.Disk;
            else if (name == "initialize") result = new SessionCoordinationState(id, shared.Marker, true);
            else if (name == "current") result = shared.Marker;
            else if (name is "begin" or "recover")
            {
                if (shared.LockedBy is not null) result = null;
                else { shared.LockedBy = id; shared.Marker = new(Guid.NewGuid().ToString(), name == "begin" ? (string)args![0]! : shared.Marker.Phase == "redirect" ? "redirect" : "transition", id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); result = shared.Marker; }
            }
            else if (name is "finish" or "abandon")
            {
                var matches = shared.Marker.Generation == (string)args![0]! && shared.LockedBy == id;
                if (matches) { shared.Marker = shared.Marker with { Phase = name == "finish" ? "stable" : "verify" }; shared.LockedBy = null; }
                result = matches;
            }
            else if (name == "park") { if (shared.LockedBy == id) shared.LockedBy = null; result = true; }
            else if (name == "releaseView") result = shared.Marker.Phase == "stable" && shared.Marker.Generation == (string)args![0]!;
            else if (name == "dispose") { if (shared.LockedBy == id) shared.LockedBy = null; result = default(T); }
            else throw new InvalidOperationException(name);
            return ValueTask.FromResult((T)result!);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
