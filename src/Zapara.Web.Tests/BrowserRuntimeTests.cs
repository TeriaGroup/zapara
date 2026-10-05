using System.Net;
using Microsoft.JSInterop;
using Xunit;
using Zapara.Contracts.Sync;
using Zapara.Web.Services;

namespace Zapara.Web.Tests;

public sealed class BrowserRuntimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task RuntimeReloadsGuestEditsCommittedByAnotherTabWithoutHttpEveryWake()
    {
        var disk = new MemoryBrowser();
        using var handler = new OfflineHandler(); using var http = new HttpClient(handler) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(disk); var state = new WebAppState(http, storage);
        await state.InitializeAsync();
        await state.StartRuntimeAsync(ct: Ct);
        try
        {
            var id = Guid.NewGuid(); var remote = new WebProfile { StorageRevision = 1 };
            remote.Records[ProfileValues.Key("friend", id)] = new() { EntityType = "friend", EntityId = id, Value = ProfileValues.Serialize(new FriendValue(null, "А101", "Из другой вкладки", 1, true)) };
            disk.Store("profiles", "guest", remote);
            var before = handler.Calls;
            await state.WakeRuntimeAsync(ct: Ct);
            Assert.Equal("Из другой вкладки", Assert.Single(state.Values<FriendValue>("friend")).Value.MemberNames);
            await state.WakeRuntimeAsync(ct: Ct);
            Assert.Equal(before, handler.Calls);
        }
        finally { await state.StopRuntimeAsync(); }
    }

    [Fact]
    public async Task ConcurrentStartHasOneTimerAndStopDetachesObserversAndIgnoresFutureWakes()
    {
        var disk = new MemoryBrowser(); var js = new ObserverBridge(); var clock = new ManualClock();
        using var http = new HttpClient(new OfflineHandler()) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(disk); var state = new WebAppState(http, storage); await state.InitializeAsync();
        await Task.WhenAll(state.StartRuntimeAsync(js, clock, Ct), state.StartRuntimeAsync(js, clock, Ct));
        await state.WakeRuntimeAsync(ct: Ct);
        Assert.Equal(1, js.Starts); Assert.Equal(1, clock.CreatedTimers);
        await Task.WhenAll(state.StopRuntimeAsync(), state.StopRuntimeAsync());
        Assert.Equal(1, js.Stops);
        disk.Store("profiles", "guest", FriendProfile("guest", 10, "После остановки"));
        clock.Advance(TimeSpan.FromHours(1)); await state.WakeRuntimeAsync(ct: Ct);
        Assert.Empty(state.Profile.Records);
    }

    [Fact]
    public async Task ClockTickNotifiesButPublicRefreshIsLimitedToItsOwnInterval()
    {
        var clock = new ManualClock(); using var handler = new OfflineHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser()); var state = new WebAppState(http, storage); await state.InitializeAsync();
        await state.StartRuntimeAsync(clock: clock, ct: Ct);
        try
        {
            await state.WakeRuntimeAsync(ct: Ct); var before = handler.Calls;
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Observe() => changed.TrySetResult();
            state.Changed += Observe; clock.Advance(TimeSpan.FromSeconds(30));
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct); state.Changed -= Observe;
            Assert.Equal(before, handler.Calls);
            clock.Advance(TimeSpan.FromMinutes(15)); await state.WakeRuntimeAsync(ct: Ct);
            Assert.Equal(before + 2, handler.Calls); // one public attempt and its unavailable built-in fallback
            await state.WakeRuntimeAsync(ct: Ct); Assert.Equal(before + 2, handler.Calls);
        }
        finally { await state.StopRuntimeAsync(); }
    }

    [Fact]
    public async Task LateDiskSnapshotDoesNotOverwriteANewerLocalCommit()
    {
        var disk = new MemoryBrowser(); var bridge = new DelayedBridge(disk);
        using var http = new HttpClient(new OfflineHandler()) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(bridge); var state = new WebAppState(http, storage); await state.InitializeAsync();
        await state.StartRuntimeAsync(ct: Ct); await state.WakeRuntimeAsync(ct: Ct);
        try
        {
            disk.Store("profiles", "guest", FriendProfile("guest", 1, "Из другой вкладки"));
            bridge.DelayNext = true;
            var wake = state.WakeRuntimeAsync(ct: Ct); await bridge.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            var id = Guid.NewGuid();
            await state.PutAsync("homework", id, new HomeworkValue("Физика", "физика", "Новая локальная запись", 1,
                new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero), null));
            bridge.Release.TrySetResult(); await wake;
            Assert.Equal("Новая локальная запись", Assert.Single(state.Values<HomeworkValue>("homework")).Value.Text);
            Assert.Single(state.Values<FriendValue>("friend")); Assert.Equal(2, state.Profile.StorageRevision);
        }
        finally { bridge.Release.TrySetResult(); await state.StopRuntimeAsync(); }
    }

    [Fact]
    public async Task LateDiskReadCannotPopulateReplacementAccountAndStopCancelsAWaitingRead()
    {
        var disk = new MemoryBrowser(); var bridge = new DelayedBridge(disk); var family = Guid.NewGuid();
        using var handler = new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/web-api/session"
            ? BrowserApiClientTests.Session(family) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var http = new HttpClient(handler) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(bridge); await using var api = new BrowserApiClient(http, storage);
        var state = new WebAppState(http, storage, api); await state.InitializeAsync();
        await state.StartRuntimeAsync(ct: Ct); await state.WakeRuntimeAsync(ct: Ct);
        try
        {
            var oldOwner = state.ProfileKey; disk.Store("profiles", oldOwner, FriendProfile(oldOwner, 1, "Старый аккаунт"));
            bridge.DelayNext = true; var first = state.WakeRuntimeAsync(ct: Ct);
            await bridge.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            family = Guid.NewGuid(); await api.RefreshSessionAsync(Ct);
            bridge.Release.TrySetResult(); await first;
            Assert.NotEqual(oldOwner, state.ProfileKey); Assert.Empty(state.Profile.Records);
            await state.WakeRuntimeAsync(ct: Ct); // drain the session-change wake before the second controlled read
            bridge.Reset(); disk.Store("profiles", state.ProfileKey, FriendProfile(state.ProfileKey, 2, "Не принимать после остановки"));
            bridge.DelayNext = true; var pending = state.WakeRuntimeAsync(ct: Ct);
            await bridge.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            await state.StopRuntimeAsync().WaitAsync(TimeSpan.FromSeconds(5), Ct);
            try { await pending; } catch (OperationCanceledException) { }
            bridge.Release.TrySetResult(); await bridge.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            Assert.Empty(state.Profile.Records);
        }
        finally { bridge.Release.TrySetResult(); await state.StopRuntimeAsync(); }
    }

    [Fact]
    public async Task TransitionMaskPreventsBootstrapAndPrivateSynchronization()
    {
        using var handler = new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/web-api/session"
            ? BrowserApiClientTests.Session(Guid.Parse("11111111-1111-4111-8111-111111111111")) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var http = new HttpClient(handler) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser()); await using var api = new BrowserApiClient(http, storage);
        var state = new WebAppState(http, storage, api); await state.InitializeAsync();
        await api.SessionCoordinationFailed(); var calls = handler.Calls;
        await state.StartRuntimeAsync(ct: Ct);
        try { await state.WakeRuntimeAsync(recheckSession: true, refreshPublic: true, ct: Ct); Assert.Equal(calls, handler.Calls); Assert.True(api.Transitioning); }
        finally { await state.StopRuntimeAsync(); }
    }

    [Fact]
    public async Task StopCancelsThePublicFetchWithoutStartingFallbackOrPublishingLater()
    {
        var clock = new ManualClock(); var block = false;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (request, ct) =>
        {
            if (block && request.RequestUri!.AbsolutePath == "/api/v1/groups")
            {
                started.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                finally { cancelled.TrySetResult(); }
            }
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        using var http = new HttpClient(handler) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser()); var state = new WebAppState(http, storage); await state.InitializeAsync();
        await state.StartRuntimeAsync(clock: clock, ct: Ct); await state.WakeRuntimeAsync(ct: Ct);
        block = true; clock.Advance(TimeSpan.FromMinutes(16));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await state.StopRuntimeAsync().WaitAsync(TimeSpan.FromSeconds(5), Ct); await cancelled.Task.WaitAsync(Ct);
        var calls = handler.Calls; clock.Advance(TimeSpan.FromHours(1)); await state.WakeRuntimeAsync(ct: Ct);
        Assert.Equal(calls, handler.Calls); Assert.Null(state.Schedule);
    }

    [Fact]
    public async Task BootstrapRechecksOnFocusAndOncePerMinuteWithoutPublicFetchEverySync()
    {
        var sessions = 0; var groups = 0; var clock = new ManualClock();
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/web-api/session")
            { sessions++; return Task.FromResult(BrowserApiClientTests.Session(Guid.Parse("11111111-1111-4111-8111-111111111111"))); }
            if (request.RequestUri.AbsolutePath == "/api/v1/groups") groups++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        using var http = new HttpClient(handler) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser()); await using var api = new BrowserApiClient(http, storage);
        var state = new WebAppState(http, storage, api); await state.InitializeAsync();
        await state.StartRuntimeAsync(clock: clock, ct: Ct);
        try
        {
            await state.WakeRuntimeAsync(ct: Ct); Assert.Equal(1, sessions); var initialGroups = groups;
            await state.BrowserRuntimeWake("focus"); Assert.Equal(2, sessions);
            await state.WakeRuntimeAsync(ct: Ct); Assert.Equal(2, sessions); Assert.Equal(initialGroups, groups);
            clock.Advance(TimeSpan.FromMinutes(1)); await state.WakeRuntimeAsync(ct: Ct);
            Assert.Equal(3, sessions); Assert.Equal(initialGroups, groups);
        }
        finally { await state.StopRuntimeAsync(); }
    }

    [Fact]
    public async Task BeforeReloadFlushRunsOutsideGateAndChangedGroupRefreshesOnlyOnce()
    {
        var disk = new MemoryBrowser(); using var handler = new OfflineHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(disk); var state = new WebAppState(http, storage); await state.InitializeAsync();
        var once = true;
        state.BeforeReload += async _ =>
        {
            if (!once) return; once = false;
            await state.PutAsync("friend", Guid.NewGuid(), new FriendValue(null, "А101", "Сохранено до чтения", 1, true));
        };
        await state.StartRuntimeAsync(ct: Ct);
        try
        {
            await state.WakeRuntimeAsync(ct: Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);
            Assert.Single(state.Values<FriendValue>("friend"));
            var latest = disk.Load<WebProfile>("profiles", "guest")!; latest.StorageRevision++;
            latest.Records[ProfileValues.Key("settings", SyncValidation.SettingsId)] = new()
            { EntityType = "settings", EntityId = SyncValidation.SettingsId, Value = ProfileValues.Serialize(new SettingsValue("А101", false, null, null, 25, false)) };
            var calls = handler.Calls; disk.Store("profiles", "guest", latest);
            await state.WakeRuntimeAsync(ct: Ct); Assert.Equal("А101", state.GroupId); Assert.Equal(calls + 2, handler.Calls);
            await state.WakeRuntimeAsync(ct: Ct); Assert.Equal(calls + 2, handler.Calls);
        }
        finally { await state.StopRuntimeAsync(); }
    }

    private static WebProfile FriendProfile(string owner, long revision, string name)
    {
        var profile = new WebProfile { Owner = owner, StorageRevision = revision }; var id = Guid.NewGuid();
        profile.Records[ProfileValues.Key("friend", id)] = new() { EntityType = "friend", EntityId = id, Value = ProfileValues.Serialize(new FriendValue(null, "А101", name, 1, true)) };
        return profile;
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Interlocked.Increment(ref Calls); return action(request, ct); }
    }
    private sealed class DelayedBridge(MemoryBrowser inner) : IJSRuntime, IJSObjectReference
    {
        internal bool DelayNext;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously), Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Reset() { Started = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously); Finished = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, CancellationToken.None, args);
        public async ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args)
        {
            if (identifier == "import") return (T)(object)this;
            var value = await inner.InvokeAsync<T>(identifier, ct, args);
            if (identifier == "read" && args?[0] as string == "profiles" && DelayNext)
            { DelayNext = false; Started.TrySetResult(); await Release.Task; Finished.TrySetResult(); }
            return value;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class ObserverBridge : IJSRuntime, IJSObjectReference
    {
        internal int Starts, Stops;
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, CancellationToken.None, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args)
        {
            if (identifier == "import") return ValueTask.FromResult((T)(object)this);
            if (identifier == "start") Starts++; if (identifier == "stop") Stops++;
            return ValueTask.FromResult(default(T)!);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        private readonly List<ManualTimer> timers = [];
        internal int CreatedTimers => timers.Count;
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var timer = new ManualTimer(this, callback, state, dueTime, period); timers.Add(timer); return timer; }
        internal void Advance(TimeSpan elapsed) { now += elapsed; foreach (var timer in timers.ToArray()) timer.Fire(); }
        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state, TimeSpan due, TimeSpan period) : ITimer
        {
            private DateTimeOffset next = clock.now + due;
            private TimeSpan interval = period;
            private bool disposed;
            internal void Fire() { if (!disposed && clock.now >= next) { next = clock.now + interval; callback(state); } }
            public bool Change(TimeSpan dueTime, TimeSpan newPeriod) { next = clock.now + dueTime; interval = newPeriod; return !disposed; }
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class OfflineHandler : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Calls); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }
    }
}
