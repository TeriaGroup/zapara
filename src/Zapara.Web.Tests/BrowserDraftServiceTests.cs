using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using Vograph.Core.Models;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Sync;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserDraftServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Created = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Latest_input_is_coalesced_and_reload_waits_for_the_durable_draft()
    {
        await using var h = new Harness();
        var opened = await h.Drafts.OpenHomeworkAsync("new", null, null, new("Математика", "", 1, null), Ct);
        h.Browser.HoldDraftWrite = true;
        var first = h.Drafts.UpdateAsync(opened.Lease, opened.Value with { Text = "Первая" }, Ct);
        await h.Browser.WriteStarted.Task.WaitAsync(Ct);
        var second = h.Drafts.UpdateAsync(opened.Lease, opened.Value with { Text = "Вторая" }, Ct);
        var newest = h.Drafts.UpdateAsync(opened.Lease, opened.Value with { Text = "Последняя" }, Ct);
        var reload = h.State.WaitForStorageAsync(Ct);
        Assert.False(reload.IsCompleted);
        h.Browser.ReleaseWrite.SetResult();
        await Task.WhenAll(first, second, newest, reload);
        Assert.Equal("Последняя", Assert.IsType<HomeworkDraft>(h.Load(opened.Lease)!.Value).Text);
        Assert.Equal(2, h.Browser.DraftWrites);
        Assert.Equal(1, h.Browser.MaximumConcurrentWrites);
        Assert.False(h.Drafts.HasPending);
    }

    [Fact]
    public async Task Account_switch_keeps_old_queued_draft_but_never_restores_it_into_new_owner()
    {
        await using var h = new Harness();
        await h.State.InitializeAsync();
        var ownerA = h.State.ProfileKey;
        var userA = h.Backend.User;
        var opened = await h.Drafts.OpenHomeworkAsync("new", null, null, new("Математика", "Текст А", 1, null), Ct);
        h.Browser.HoldDraftWrite = true;
        var writing = h.Drafts.UpdateAsync(opened.Lease, opened.Value, Ct);
        await h.Browser.WriteStarted.Task.WaitAsync(Ct);
        h.Backend.User = Guid.NewGuid(); h.Backend.Family = Guid.NewGuid();
        await h.Api.RefreshSessionAsync(Ct);
        Assert.False(h.Drafts.IsCurrent(opened.Lease));
        var b = await h.Drafts.OpenHomeworkAsync("new", null, null, new("Физика", "", 1, null), Ct);
        Assert.False(b.Restored);
        h.Browser.ReleaseWrite.SetResult(); await writing;
        Assert.Equal(ownerA, h.Load(opened.Lease)!.Owner);
        Assert.Null(h.Load(b.Lease));
        h.Backend.User = userA; h.Backend.Family = Guid.NewGuid(); await h.Api.RefreshSessionAsync(Ct);
        var restored = await h.Drafts.OpenHomeworkAsync("new", null, null, new("", "", 1, null), Ct);
        Assert.True(restored.Restored);
        Assert.Equal("Текст А", restored.Value.Text);
        Assert.Equal(opened.Lease.TargetId, restored.Lease.TargetId);
    }

    [Fact]
    public async Task New_session_for_same_owner_invalidates_old_editor_without_deleting_its_draft()
    {
        await using var h = new Harness();
        await h.State.InitializeAsync();
        var opened = await h.Drafts.OpenFriendAsync("new", null, null, new("", "g", "А101", "Маша", 2, null), Ct);
        await h.Drafts.UpdateAsync(opened.Lease, opened.Value, Ct);
        h.Backend.Family = Guid.NewGuid();
        await h.Api.RefreshSessionAsync(Ct);
        Assert.Equal(opened.Lease.Scope.Owner, h.State.ProfileKey);
        Assert.False(h.Drafts.IsCurrent(opened.Lease));
        var error = await Assert.ThrowsAsync<BrowserApiException>(() => h.Drafts.UpdateAsync(opened.Lease, opened.Value with { Names = "Старое окно" }, Ct));
        Assert.Equal("account_changed", error.Code);
        var restored = await h.Drafts.OpenFriendAsync("new", null, null, new("", "", "", "", 1, null), Ct);
        Assert.True(restored.Restored);
        Assert.Equal("Маша", restored.Value.Names);
        Assert.True(h.Drafts.IsCurrent(restored.Lease));
    }

    [Fact]
    public async Task Same_tab_reload_restores_while_another_tab_has_its_own_slot()
    {
        var disk = new Dictionary<string, string>();
        var tab = Guid.NewGuid().ToString("D");
        await using (var first = new Harness(disk, tab))
        {
            var opened = await first.Drafts.OpenFriendAsync("new", null, null, new("", "g", "А101", "Маша", 2, null), Ct);
            await first.Drafts.UpdateAsync(opened.Lease, opened.Value, Ct);
        }
        await using var reload = new Harness(disk, tab);
        Assert.True((await reload.Drafts.OpenFriendAsync("new", null, null, new("", "", "", "", 1, null), Ct)).Restored);
        await using var other = new Harness(disk);
        Assert.False((await other.Drafts.OpenFriendAsync("new", null, null, new("", "", "", "", 1, null), Ct)).Restored);
    }

    [Fact]
    public async Task Stale_original_and_creation_metadata_require_explicit_rebase()
    {
        await using var h = new Harness();
        var id = Guid.NewGuid().ToString("D");
        var original = new HomeworkValue("Математика", "математика", "Старая запись", 1, Created, null);
        var opened = await h.Drafts.OpenHomeworkAsync(id, 3, original, new(original.SubjectRaw, "Мой текст", 2, original), Ct);
        await h.Drafts.UpdateAsync(opened.Lease, opened.Value, Ct);
        var current = new HomeworkValue("Математика", "математика", "Новая запись", 4, Created.AddDays(1), new(2026, 9, 2));
        var restored = await h.Drafts.OpenHomeworkAsync(id, 4, current, new(current.SubjectRaw, current.Text, 4, current), Ct);
        Assert.True(restored.Stale);
        Assert.Equal("Мой текст", restored.Value.Text);
        Assert.Equal(Created, restored.Value.Original!.CreatedAtUtc);
        Assert.Equal(3, restored.Lease.Baseline.Revision);
        var accepted = h.Drafts.Rebase(restored.Lease, 4, current);
        await h.Drafts.UpdateAsync(accepted, restored.Value with { Original = current }, Ct);
        Assert.False(BrowserDraftService.IsStale(accepted, 4, current));
        Assert.Equal(Created.AddDays(1), Assert.IsType<HomeworkDraft>(h.Load(accepted)!.Value).Original!.CreatedAtUtc);
    }

    [Fact]
    public async Task Cancellation_or_quota_keeps_queued_input_and_blocks_reload_until_flush_succeeds()
    {
        await using var h = new Harness();
        var opened = await h.Drafts.OpenHomeworkAsync("new", null, null, new("Математика", "Не потерять", 1, null), Ct);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Drafts.UpdateAsync(opened.Lease, opened.Value, cancel.Token));
        Assert.True(h.Drafts.HasPending);
        Assert.Equal(0, h.Browser.DraftWrites);
        h.Browser.FailDraftWrites = true;
        await Assert.ThrowsAsync<JSException>(() => h.State.WaitForStorageAsync(Ct));
        Assert.True(h.Drafts.HasPending);
        h.Browser.FailDraftWrites = false;
        await h.State.WaitForStorageAsync(Ct);
        Assert.Equal("Не потерять", Assert.IsType<HomeworkDraft>(h.Load(opened.Lease)!.Value).Text);
        await h.Drafts.ClearAsync(opened.Lease, Ct);
        Assert.Null(h.Load(opened.Lease));
        Assert.False(h.Drafts.HasPending);
    }

    [Fact]
    public async Task Wrong_feature_payload_is_rejected_before_any_persistence()
    {
        await using var h = new Harness();
        var opened = await h.Drafts.OpenHomeworkAsync("new", null, null, new("", "", 1, null), Ct);
        await Assert.ThrowsAsync<ArgumentException>(() => h.Drafts.UpdateAsync(opened.Lease, new FriendDraft("", "g", "А101", "Маша", 2, null), Ct));
        Assert.Equal(0, h.Browser.DraftWrites);
    }

    [Theory]
    [InlineData("Высшая математика", "Высшая математика")]
    [InlineData("Программирование на C", "Программирование на C")]
    [InlineData("лек Высшая математика", "Высшая математика")]
    public void Homework_title_strips_only_a_real_type_prefix(string raw, string expected)
    {
        var type = EditorSubjectTitles.TypeOf(raw, Array.Empty<Lesson>(), "g");
        Assert.Equal(expected, WebAppState.SubjectTitle(raw, type));
    }

    private sealed class Harness : IAsyncDisposable
    {
        public DraftBrowser Browser { get; }
        public SessionBackend Backend { get; } = new();
        private readonly HttpClient http;
        private readonly BrowserStorage storage;
        public BrowserApiClient Api { get; }
        public WebAppState State { get; }
        public BrowserDraftService Drafts { get; }
        public Harness(Dictionary<string, string>? disk = null, string? tab = null)
        {
            Browser = new(disk ?? [], tab ?? Guid.NewGuid().ToString("D"));
            Browser.Disk["public:schedule"] = JsonSerializer.Serialize(new PublicCache(new(new(2026, 9, 1), 2, [], []), "Осень", true), BrowserStorage.Json);
            http = new(Backend) { BaseAddress = new("https://zapara.test/app/") };
            storage = new(Browser); Api = new(http, storage); State = new(http, storage, Api); Drafts = new(storage, Browser, State, Api);
        }
        public StoredEditorDraft? Load(DraftLease lease) => Browser.Disk.TryGetValue("preferences:" + BrowserDraftService.Key(lease), out var value) ? JsonSerializer.Deserialize<StoredEditorDraft>(value, BrowserStorage.Json) : null;
        public async ValueTask DisposeAsync() { await Drafts.DisposeAsync(); await Api.DisposeAsync(); await storage.DisposeAsync(); http.Dispose(); }
    }
    private sealed class SessionBackend : HttpMessageHandler
    {
        public Guid User = Guid.NewGuid(), Family = Guid.NewGuid();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => request.RequestUri!.AbsolutePath == "/web-api/session"
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new BrowserSession(true, new UserResponse(User, "test.user", null, Created), Family, new string('a', 43), new())) })
            : throw new HttpRequestException("offline public catalog");
    }
    private sealed class DraftBrowser(Dictionary<string, string> disk, string tabId) : IJSRuntime, IJSObjectReference
    {
        public Dictionary<string, string> Disk { get; } = disk;
        public bool HoldDraftWrite, FailDraftWrites;
        public int DraftWrites, MaximumConcurrentWrites;
        private int active;
        public TaskCompletionSource WriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => InvokeAsync<T>(id, CancellationToken.None, args);
        public async ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args)
        {
            if (id == "import") return (T)(object)this;
            if (id == "tabId") return (T)(object)tabId;
            if (id == "read") return (T)(object?)Disk.GetValueOrDefault(args![0] + ":" + args[1])!;
            if (id == "write")
            {
                var key = args![0] + ":" + args[1];
                if (((string)args[1]!).StartsWith("draft:", StringComparison.Ordinal))
                {
                    if (FailDraftWrites) throw new JSException("QuotaExceededError");
                    DraftWrites++; active++; MaximumConcurrentWrites = Math.Max(active, MaximumConcurrentWrites);
                    try { if (HoldDraftWrite) { HoldDraftWrite = false; WriteStarted.TrySetResult(); await ReleaseWrite.Task.WaitAsync(Ct); } Disk[key] = (string)args[2]!; }
                    finally { active--; }
                }
                else Disk[key] = (string)args[2]!;
            }
            return default!;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
