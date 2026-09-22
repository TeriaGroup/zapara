using System.Net;
using System.Net.Http.Json;
using Microsoft.JSInterop;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserNotificationsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Update_ready_event_supersedes_only_the_update_check_notice()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Notifications.CheckUpdateAsync(Ct);
        var pendingNotice = fixture.Notifications.Message;
        Assert.False(fixture.Notifications.Browser.UpdateAvailable);
        Assert.NotNull(pendingNotice);

        await fixture.Notifications.UpdateAvailableChanged(true);
        Assert.True(fixture.Notifications.Browser.UpdateAvailable);
        Assert.NotEqual(pendingNotice, fixture.Notifications.Message);
        Assert.Contains("готова", fixture.Notifications.Message);

        await fixture.Notifications.UpdateAvailableChanged(false);
        Assert.False(fixture.Notifications.Browser.UpdateAvailable);
        Assert.Equal(pendingNotice, fixture.Notifications.Message);
    }

    [Fact]
    public async Task Update_events_preserve_a_later_notification_delivery_result()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Notifications.InitializeAsync(Ct);
        await fixture.Notifications.EnableAsync(Ct);
        await fixture.Notifications.CheckUpdateAsync(Ct);
        await fixture.Notifications.TestAsync(Ct);
        Assert.Null(fixture.Notifications.Error);
        var deliveryMessage = fixture.Notifications.Message;
        Assert.Contains("службе доставки", deliveryMessage);
        await fixture.Notifications.UpdateAvailableChanged(true);
        Assert.Equal(deliveryMessage, fixture.Notifications.Message);
        await fixture.Notifications.UpdateAvailableChanged(false);
        Assert.Equal(deliveryMessage, fixture.Notifications.Message);
    }

    [Fact]
    public async Task An_older_check_reply_cannot_overwrite_a_newer_ready_event()
    {
        await using var fixture = await Fixture.Create();
        fixture.Js.PauseCheckUpdate = true;
        var checking = fixture.Notifications.CheckUpdateAsync(Ct);
        await fixture.Js.CheckStarted.Task.WaitAsync(Ct);
        await fixture.Notifications.UpdateAvailableChanged(true);
        fixture.Js.CheckResume.SetResult();
        await checking;
        Assert.True(fixture.Notifications.Browser.UpdateAvailable);
        Assert.Contains("готова", fixture.Notifications.Message);
    }

    [Fact]
    public async Task Offline_cold_start_preserves_existing_binding_until_authentication_is_known()
    {
        await using var fixture = await Fixture.Create(bootstrap: false);
        fixture.FailSession = true;
        fixture.Js.Subscribed = true;
        fixture.Disk.Store("preferences", "notifications-binding", new PushBinding(fixture.Family, fixture.SubscriptionId));
        await Assert.ThrowsAsync<BrowserApiException>(() => fixture.Api.RefreshSessionAsync(Ct));
        Assert.False(fixture.Api.Available);
        await fixture.Notifications.InitializeAsync(Ct);
        Assert.True(fixture.Js.Subscribed);
        Assert.Equal(fixture.Family, fixture.Disk.Load<PushBinding>("preferences", "notifications-binding")!.FamilyId);
        Assert.False(fixture.Notifications.Enabled);
        Assert.Equal(0, fixture.Posts);
    }

    [Fact]
    public async Task Unowned_browser_subscription_is_disconnected_before_a_new_account_can_use_it()
    {
        await using var fixture = await Fixture.Create();
        fixture.Js.Subscribed = true;
        await fixture.Notifications.SessionChangedAsync(Ct);
        Assert.False(fixture.Js.Subscribed);
        Assert.False(fixture.Notifications.Enabled);
        Assert.Equal(0, fixture.Posts);
    }

    [Fact]
    public async Task Family_change_unsubscribes_previous_binding_without_rebinding_it()
    {
        await using var fixture = await Fixture.Create();
        fixture.Js.Subscribed = true;
        fixture.Disk.Store("preferences", "notifications-binding", new PushBinding(fixture.Family, fixture.SubscriptionId));
        await fixture.Notifications.InitializeAsync(Ct);
        Assert.True(fixture.Notifications.Enabled);
        fixture.Family = Guid.NewGuid();
        await fixture.Api.RefreshSessionAsync(Ct);
        await fixture.Notifications.SessionChangedAsync(Ct);
        Assert.False(fixture.Js.Subscribed);
        Assert.Null(fixture.Disk.Load<PushBinding>("preferences", "notifications-binding"));
        Assert.False(fixture.Notifications.Enabled);
        Assert.Equal(0, fixture.Posts);
    }

    [Fact]
    public async Task Account_switch_while_permission_prompt_is_open_never_posts_as_new_family()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Notifications.InitializeAsync(Ct);
        fixture.Js.PauseSubscribe = true;
        var enabling = fixture.Notifications.EnableAsync(Ct);
        await fixture.Js.Started.Task.WaitAsync(Ct);
        fixture.Family = Guid.NewGuid();
        await fixture.Api.RefreshSessionAsync(Ct);
        fixture.Js.Resume.SetResult();
        await enabling;
        Assert.Equal(0, fixture.Posts);
        Assert.False(fixture.Js.Subscribed);
        Assert.NotNull(fixture.Notifications.Error);
        Assert.Null(fixture.Disk.Load<PushBinding>("preferences", "notifications-binding"));
    }

    [Fact]
    public async Task Missing_server_configuration_never_prompts_or_claims_enabled()
    {
        await using var fixture = await Fixture.Create();
        fixture.Available = false;
        await fixture.Notifications.InitializeAsync(Ct);
        await fixture.Notifications.EnableAsync(Ct);
        Assert.Equal(0, fixture.Js.SubscribeCalls);
        Assert.False(fixture.Notifications.Enabled);
        Assert.NotNull(fixture.Notifications.Error);
    }

    [Fact]
    public async Task Registration_failure_rolls_back_new_browser_subscription()
    {
        await using var fixture = await Fixture.Create();
        fixture.RejectPost = true;
        await fixture.Notifications.InitializeAsync(Ct);
        await fixture.Notifications.EnableAsync(Ct);
        Assert.Equal(1, fixture.Posts);
        Assert.False(fixture.Js.Subscribed);
        Assert.False(fixture.Notifications.Enabled);
        Assert.NotNull(fixture.Notifications.Error);
    }

    [Fact]
    public async Task Logout_cleanup_deletes_current_family_subscription_then_unsubscribes_locally()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Notifications.InitializeAsync(Ct);
        await fixture.Notifications.EnableAsync(Ct);
        Assert.True(fixture.Notifications.Enabled);
        await fixture.Notifications.PrepareLogoutAsync(Ct);
        Assert.Equal(1, fixture.Deletes);
        Assert.False(fixture.Js.Subscribed);
        Assert.Null(fixture.Disk.Load<PushBinding>("preferences", "notifications-binding"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Family = Guid.NewGuid();
        public Guid SubscriptionId = Guid.NewGuid();
        public bool Available = true, RejectPost, FailSession;
        public int Posts, Deletes;
        public MemoryBrowser Disk { get; } = new();
        public PushBrowser Js { get; } = new();
        public BrowserApiClient Api { get; private set; } = null!;
        public BrowserNotifications Notifications { get; private set; } = null!;
        private HttpClient http = null!;
        private BrowserStorage storage = null!;
        public static async Task<Fixture> Create(bool bootstrap = true)
        {
            var fixture = new Fixture();
            fixture.http = new(new Handler(fixture.Send)) { BaseAddress = new("https://zapara.test/app/") };
            fixture.storage = new(fixture.Disk);
            fixture.Api = new(fixture.http, fixture.storage);
            fixture.Notifications = new(fixture.Js, fixture.Api, fixture.storage, new(fixture.http, fixture.storage));
            if (bootstrap) await fixture.Api.RefreshSessionAsync(Ct);
            return fixture;
        }
        private HttpResponseMessage Send(HttpRequestMessage request)
        {
            if (request.RequestUri!.AbsolutePath == "/web-api/session") { if (FailSession) throw new HttpRequestException(); return BrowserApiClientTests.Session(Family); }
            if (request.RequestUri.AbsolutePath.EndsWith("/capabilities")) return new(HttpStatusCode.OK) { Content = JsonContent.Create(new PushCapabilities(Available, "public-key", Available ? null : "push_unavailable")) };
            if (request.RequestUri.AbsolutePath == "/web-api/notifications/test") return new(HttpStatusCode.Accepted) { Content = JsonContent.Create(new PushTestResult("accepted")) };
            if (request.Method == HttpMethod.Post)
            {
                Posts++;
                Assert.Equal(Family.ToString(), request.Headers.GetValues("X-Zapara-Family").Single());
                if (RejectPost) return new(HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(new { code = "push_unavailable" }) };
                return new(HttpStatusCode.Created) { Content = JsonContent.Create(new PushSubscriptionInfo(SubscriptionId, true, "Europe/Moscow", ["20:00", "07:30"])) };
            }
            if (request.Method == HttpMethod.Delete) { Deletes++; return new(HttpStatusCode.NoContent); }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { new PushSubscriptionInfo(SubscriptionId, true, "Europe/Moscow", ["20:00", "07:30"]) }) };
        }
        public async ValueTask DisposeAsync() { await Notifications.DisposeAsync(); await storage.DisposeAsync(); http.Dispose(); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
    private sealed class PushBrowser : IJSRuntime, IJSObjectReference
    {
        public bool Subscribed, PauseSubscribe, PauseCheckUpdate;
        public int SubscribeCalls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CheckStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CheckResume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, CancellationToken.None, args);
        public async ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args)
        {
            if (identifier == "import") return (T)(object)this;
            if (identifier == "watch") return (T)(object)true;
            if (identifier == "inspect") return (T)(object)new BrowserPushStatus(true, true, "granted", false, false, "Europe/Moscow", true, Subscribed);
            if (identifier == "unsubscribe") { Subscribed = false; return (T)(object)true; }
            if (identifier == "checkUpdate")
            {
                CheckStarted.TrySetResult();
                if (PauseCheckUpdate) await CheckResume.Task.WaitAsync(ct);
                return (T)(object)false;
            }
            if (identifier == "subscribe")
            {
                SubscribeCalls++; Started.TrySetResult();
                if (PauseSubscribe) await Resume.Task.WaitAsync(ct);
                var created = !Subscribed; Subscribed = true;
                return (T)(object)new BrowserSubscribeResult(created, new("https://push.test/subscription", new("key", "auth"), true, "Europe/Moscow"));
            }
            return default!;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
