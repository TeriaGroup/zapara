using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Communities;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserCommunityCacheTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("member")]
    [InlineData("null")]
    [InlineData("removed")]
    public async Task Successful_list_permission_loss_fences_an_older_staff_write_even_without_selected_detail(string loss)
    {
        using var server = new Backend(); var disk = new Disk();
        await using (var first = await View.Open(server, disk))
        await using (var second = await View.Open(server, disk))
        {
            await first.Service.LoadAsync(); await first.Service.OpenAsync(server.CommunityId);
            Assert.True(first.Service.IsStaff); Assert.Single(first.Service.JoinRequests);
            Assert.Null(second.Service.Selected); Assert.Empty(second.Service.Communities);
            disk.HoldCacheWrite = true;
            var oldRead = first.Service.OpenAsync(server.CommunityId);
            await disk.WriteStarted.Task.WaitAsync(Ct);
            if (loss == "member") server.Role = "member";
            else if (loss == "null") server.Role = null;
            else server.ExcludeCommunity = true;
            await second.Service.LoadAsync();
            Assert.False(second.Service.IsStaff); Assert.Null(second.Service.Selected);
            disk.ReleaseWrite.SetResult(); await oldRead;
        }
        server.Offline = true;
        await using var reload = await View.Open(server, disk);
        await reload.Service.LoadAsync(); await reload.Service.OpenAsync(server.CommunityId);
        Assert.False(reload.Service.IsStaff);
        Assert.Empty(reload.Service.JoinRequests);
        Assert.DoesNotContain(reload.Service.Communities, c => c.Role is "headman" or "curator");
        Assert.Empty(reload.Service.Announcements);
        Assert.False(reload.Service.CanPublish); Assert.False(reload.Service.CanWrite);
    }

    [Fact]
    public async Task Confirmed_role_downgrade_drops_the_previous_staff_detail_before_offline_reload()
    {
        using var server = new Backend(); var disk = new Disk();
        await using (var live = await View.Open(server, disk))
        {
            await live.Service.LoadAsync(); await live.Service.OpenAsync(server.CommunityId);
            Assert.True(live.Service.IsStaff);
            server.Role = "member"; await live.Service.LoadAsync();
        }
        server.Offline = true;
        await using var reload = await View.Open(server, disk); await reload.Service.LoadAsync(); await reload.Service.OpenAsync(server.CommunityId);
        Assert.False(reload.Service.IsStaff); Assert.Empty(reload.Service.JoinRequests); Assert.Empty(reload.Service.Announcements);
    }

    [Fact]
    public async Task Cached_read_in_a_live_session_still_blocks_every_write_after_network_failure()
    {
        using var server = new Backend(); var disk = new Disk();
        await using var live = await View.Open(server, disk);
        await live.Service.LoadAsync(); await live.Service.OpenAsync(server.CommunityId);
        server.Offline = true; await live.Service.OpenAsync(server.CommunityId);
        Assert.True(live.Api.Session.Authenticated); Assert.True(live.Service.IsCached);
        Assert.False(live.Service.CanJoin); Assert.False(live.Service.CanWrite); Assert.False(live.Service.CanPublish);
        Assert.False(await live.Service.SaveAnnouncementAsync(server.CommunityId, server.Session.FamilyId!.Value, "Не отправлять", "Текст"));
        Assert.False(await live.Service.JoinAsync(server.CommunityId, server.Session.FamilyId.Value));
        Assert.False(await live.Service.VoteAsync(server.CommunityId, server.Session.FamilyId.Value, live.Service.Polls.Single(), server.OptionA));
        Assert.Equal(0, server.Mutations);
    }

    [Fact]
    public async Task Unconfirmed_shared_session_generation_cannot_reveal_the_saved_owner()
    {
        using var server = new Backend(); var disk = new Disk();
        await using (var live = await View.Open(server, disk)) { await live.Service.LoadAsync(); await live.Service.OpenAsync(server.CommunityId); }
        disk.Marker = disk.Marker with { Generation = Guid.NewGuid().ToString("D") }; server.Offline = true;
        await using var offline = await View.Open(server, disk); await offline.Service.LoadAsync();
        Assert.True(offline.State.IsGuest); Assert.False(offline.Service.CanRead); Assert.Empty(offline.Service.Communities);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("version")]
    [InlineData("oversize")]
    public async Task Mismatched_or_oversized_cache_is_rejected_without_publication(string corruption)
    {
        using var server = new Backend(); var disk = new Disk();
        await using (var live = await View.Open(server, disk)) { await live.Service.LoadAsync(); await live.Service.OpenAsync(server.CommunityId); }
        disk.CorruptCache(json => { if (corruption == "owner") json["owner"] = "account@https://zapara.test#" + Guid.NewGuid(); else if (corruption == "version") json["version"] = 99; else json["padding"] = new string('x', 2 * 1024 * 1024); });
        server.Offline = true;
        await using var offline = await View.Open(server, disk); await offline.Service.LoadAsync(); await offline.Service.OpenAsync(server.CommunityId);
        Assert.False(offline.Service.CanRead); Assert.Empty(offline.Service.Announcements);
    }

    [Fact]
    public async Task Cache_io_failure_keeps_current_last_good_content_readonly()
    {
        using var server = new Backend(); var disk = new Disk();
        await using var live = await View.Open(server, disk); await live.Service.LoadAsync(); await live.Service.OpenAsync(server.CommunityId);
        disk.FailCacheReads = true; server.Offline = true; await live.Service.OpenAsync(server.CommunityId);
        Assert.Equal("Разрешено для А", Assert.Single(live.Service.Announcements).Body);
        Assert.True(live.Service.IsCached); Assert.False(live.Service.CanWrite);
    }

    [Fact]
    public async Task Late_cache_write_from_another_tab_cannot_undo_confirmed_revocation()
    {
        using var server = new Backend(); var disk = new Disk();
        await using (var first = await View.Open(server, disk))
        await using (var second = await View.Open(server, disk))
        {
            await first.Service.LoadAsync(); await first.Service.OpenAsync(server.CommunityId);
            disk.HoldCacheWrite = true;
            var oldRead = first.Service.OpenAsync(server.CommunityId); await disk.WriteStarted.Task.WaitAsync(Ct);
            server.Revoked = true; await second.Service.OpenAsync(server.CommunityId);
            disk.ReleaseWrite.SetResult(); await oldRead;
        }
        server.Offline = true;
        await using var reload = await View.Open(server, disk); await reload.Service.LoadAsync(); await reload.Service.OpenAsync(server.CommunityId);
        Assert.False(reload.Service.IsCached); Assert.Empty(reload.Service.Announcements);
    }

    [Fact]
    public async Task Authorized_read_survives_offline_reload_with_all_mutations_disabled()
    {
        using var server = new Backend(); var disk = new Disk();
        var family = server.Session.FamilyId!.Value;
        await using (var online = await View.Open(server, disk)) { await online.Service.LoadAsync(); await online.Service.OpenAsync(server.CommunityId); Assert.Single(online.Service.Announcements); }
        server.Offline = true;
        await using var offline = await View.Open(server, disk);
        await offline.Service.LoadAsync(); await offline.Service.OpenAsync(server.CommunityId);
        Assert.False(offline.Api.Session.Authenticated);
        Assert.True(offline.Service.CanRead); Assert.True(offline.Service.IsCached);
        Assert.Equal("Разрешено для А", Assert.Single(offline.Service.Announcements).Body);
        Assert.Single(offline.Service.Homework); var poll = Assert.Single(offline.Service.Polls);
        Assert.False(offline.Service.CanWrite); Assert.False(offline.Service.CanPublish); Assert.False(offline.Service.CanJoin); Assert.False(offline.Service.CanVote(poll));
        Assert.False(await offline.Service.JoinAsync(server.CommunityId, family));
        Assert.False(await offline.Service.ResolveJoinAsync(server.CommunityId, Guid.NewGuid(), true, family));
        Assert.False(await offline.Service.CompleteAsync(server.CommunityId, family, offline.Service.Homework.Single(), true));
        Assert.False(await offline.Service.VoteAsync(server.CommunityId, family, poll, poll.Options[0].OptionId));
        Assert.False(await offline.Service.SaveHomeworkAsync(server.CommunityId, family, "Не писать", "Текст"));
        Assert.False(await offline.Service.SaveAnnouncementAsync(server.CommunityId, family, "Не писать", "Текст"));
        Assert.False(await offline.Service.PublishPollAsync(server.CommunityId, family, "Вопрос", DateTimeOffset.UtcNow.AddDays(1), ["А", "Б"]));
        Assert.Equal(0, server.Mutations);
        Assert.DoesNotContain(disk.CacheWrites.Values, value => value.Contains(new string('a', 43), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Logout_and_another_user_never_restore_previous_owners_cache()
    {
        using var server = new Backend(); var disk = new Disk();
        await using (var a = await View.Open(server, disk))
        {
            await a.Service.LoadAsync(); await a.Service.OpenAsync(server.CommunityId);
            await a.Api.SignOutAsync(Ct);
            Assert.False(a.Service.CanRead); Assert.Empty(a.Service.Announcements);
            server.NextSession = Backend.SessionFor(Guid.NewGuid());
            await a.Api.SignInAsync("other", "unused", ct: Ct);
            Assert.Empty(a.Service.Announcements);
        }
        server.Offline = true;
        await using var b = await View.Open(server, disk); await b.Service.LoadAsync(); await b.Service.OpenAsync(server.CommunityId);
        Assert.False(b.Service.CanRead); Assert.Empty(b.Service.Announcements);
    }

    [Fact]
    public async Task New_family_for_same_owner_requires_a_new_authorized_read_before_offline_restore()
    {
        using var server = new Backend(); var disk = new Disk();
        await using (var a = await View.Open(server, disk))
        {
            await a.Service.LoadAsync(); await a.Service.OpenAsync(server.CommunityId);
            server.NextSession = Backend.SessionFor(server.Session.User!.UserId);
            await a.Api.SignInAsync("same", "unused", ct: Ct);
            Assert.Empty(a.Service.Announcements);
        }
        server.Offline = true;
        await using var fresh = await View.Open(server, disk); await fresh.Service.LoadAsync();
        Assert.False(fresh.Service.CanRead); Assert.Empty(fresh.Service.Communities);
    }

    [Fact]
    public async Task Confirmed_access_revocation_invalidates_cache_before_an_offline_reload()
    {
        using var server = new Backend(); var disk = new Disk();
        await using (var live = await View.Open(server, disk))
        {
            await live.Service.LoadAsync(); await live.Service.OpenAsync(server.CommunityId);
            server.Revoked = true; await live.Service.OpenAsync(server.CommunityId);
            Assert.Empty(live.Service.Announcements); Assert.False(live.Service.CanWrite);
        }
        server.Offline = true;
        await using var offline = await View.Open(server, disk); await offline.Service.LoadAsync(); await offline.Service.OpenAsync(server.CommunityId);
        Assert.Empty(offline.Service.Announcements); Assert.False(offline.Service.IsCached);
    }

    [Fact]
    public async Task Cache_quota_failure_preserves_previous_good_authorized_copy()
    {
        using var server = new Backend(); var disk = new Disk();
        await using (var live = await View.Open(server, disk))
        {
            await live.Service.LoadAsync(); await live.Service.OpenAsync(server.CommunityId);
            disk.FailCacheWrites = true; server.Body = "Новый текст"; await live.Service.OpenAsync(server.CommunityId);
            Assert.Equal("Новый текст", Assert.Single(live.Service.Announcements).Body);
            Assert.NotNull(live.Service.Notice);
        }
        disk.FailCacheWrites = false; server.Offline = true;
        await using var offline = await View.Open(server, disk); await offline.Service.LoadAsync(); await offline.Service.OpenAsync(server.CommunityId);
        Assert.Equal("Разрешено для А", Assert.Single(offline.Service.Announcements).Body);
    }

    private sealed class View : IAsyncDisposable
    {
        private readonly HttpClient http; private readonly BrowserStorage storage;
        public BrowserApiClient Api { get; } public WebAppState State { get; } public BrowserCommunityService Service { get; }
        private View(Backend backend, Disk disk) { http = new(backend, false) { BaseAddress = new("https://zapara.test/app/") }; storage = new(disk); Api = new(http, storage); State = new(http, storage, Api); Service = new(Api, storage, State); }
        public static async Task<View> Open(Backend backend, Disk disk) { var view = new View(backend, disk); await view.Api.InitializeBrowserCoordinationAsync(); await view.State.InitializeAsync(); return view; }
        public async ValueTask DisposeAsync() { Service.Dispose(); await Api.DisposeAsync(); await storage.DisposeAsync(); http.Dispose(); }
    }
    private sealed class Disk : IJSRuntime, IJSObjectReference
    {
        private readonly MemoryBrowser inner = new(); public SessionMarker Marker = SessionMarker.Initial;
        private readonly string tab = Guid.NewGuid().ToString("D"); public bool FailCacheWrites, FailCacheReads, HoldCacheWrite;
        public TaskCompletionSource WriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Dictionary<string, string> CacheWrites { get; } = [];
        public void CorruptCache(Action<JsonObject> corrupt)
        {
            var entry = CacheWrites.Single(p => !p.Key.EndsWith(":fence", StringComparison.Ordinal));
            var json = JsonNode.Parse(entry.Value)!.AsObject(); corrupt(json);
            inner.Store("preferences", entry.Key, JsonSerializer.Deserialize<JsonElement>(json.ToJsonString()));
        }
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => InvokeAsync<T>(id, CancellationToken.None, args);
        public ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args)
        {
            object? value;
            if (id == "import") value = this;
            else if (id == "initialize") value = new SessionCoordinationState(tab, Marker, true);
            else if (id == "current") value = Marker;
            else if (id is "begin" or "recover") { Marker = new(Guid.NewGuid().ToString("D"), "transition", tab, 1); value = Marker; }
            else if (id is "finish" or "abandon") { Marker = Marker with { Phase = id == "finish" ? "stable" : "verify" }; value = true; }
            else if (id == "releaseView") value = Marker.Phase == "stable";
            else if (id is "dispose" or "park") value = default(T);
            else
            {
                if (id == "read" && FailCacheReads && ((string)args![1]!).StartsWith("community-cache:", StringComparison.Ordinal)) throw new JSException("UnknownError");
                if (id == "write" && ((string)args![1]!).StartsWith("community-cache:", StringComparison.Ordinal))
                {
                    if (FailCacheWrites) throw new JSException("QuotaExceededError");
                    if (HoldCacheWrite && !((string)args[1]!).EndsWith(":fence", StringComparison.Ordinal) && (string)args[2]! != "null") { HoldCacheWrite = false; return new(HeldWrite<T>(id, ct, args)); }
                    CacheWrites[(string)args[1]!] = (string)args[2]!;
                }
                return inner.InvokeAsync<T>(id, ct, args);
            }
            return ValueTask.FromResult((T)value!);
        }
        private async Task<T> HeldWrite<T>(string id, CancellationToken ct, object?[] args) { WriteStarted.SetResult(); await ReleaseWrite.Task.WaitAsync(Ct); CacheWrites[(string)args[1]!] = (string)args[2]!; return await inner.InvokeAsync<T>(id, ct, args); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Backend : HttpMessageHandler
    {
        public BrowserSession Session = SessionFor(Guid.NewGuid()); public BrowserSession? NextSession;
        public Guid CommunityId = Guid.NewGuid(), HomeworkId = Guid.NewGuid(), AnnouncementId = Guid.NewGuid(), PollId = Guid.NewGuid(), OptionA = Guid.NewGuid(), OptionB = Guid.NewGuid();
        public bool Offline, Revoked, ExcludeCommunity; public int Mutations; public string Body = "Разрешено для А"; public string? Role = "headman";
        private readonly Guid applicant = Guid.NewGuid(), joinRequest = Guid.NewGuid();
        public static BrowserSession SessionFor(Guid user) => new(true, new UserResponse(user, "test.user", "Участник", DateTimeOffset.UtcNow), Guid.NewGuid(), new string('a', 43), new());
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get) Mutations++;
            if (Offline) throw new HttpRequestException("offline");
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/web-api/auth/logout") { Session = new(false, null, null, new string('a', 43), new()); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
            if (path == "/web-api/auth/login") { Session = NextSession!; return Json(Session); }
            if (path == "/web-api/session") return Json(Session);
            if (!path.StartsWith("/web-api/communities", StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            if (Revoked) return Json(new CommunityError("Недоступно", 403, "forbidden"), 403);
            var community = new CommunityResponse(CommunityId, "Сообщество А", "Описание", 1, Role);
            var now = DateTimeOffset.UtcNow;
            if (path == "/web-api/communities") return Json(ExcludeCommunity ? Array.Empty<CommunityResponse>() : [community]);
            if (path == $"/web-api/communities/{CommunityId:D}") return Json(community);
            if (path.EndsWith("/join-request")) return Json(new OwnJoinRequestResponse(null));
            if (path.EndsWith("/join-requests")) return Json(Role is "headman" or "curator" ? new[] { new JoinRequestResponse(joinRequest, CommunityId, applicant, "pending", now) } : Array.Empty<JoinRequestResponse>());
            if (path.EndsWith("/homework")) return Json(new[] { new HomeworkResponse(HomeworkId, CommunityId, "Задание", "Текст", 1, now, now) });
            if (path.EndsWith("/completion")) return Json(new CompletionResponse(HomeworkId, false, 0, null));
            if (path.EndsWith("/announcements")) return Json(new[] { new AnnouncementResponse(AnnouncementId, CommunityId, "Объявление", Body, 1, now, now) });
            if (path.EndsWith("/polls")) return Json(new[] { new PollResponse(PollId, CommunityId, "Вопрос", now.AddDays(1), 1, [new(OptionA, "А", 1), new(OptionB, "Б", 2)]) });
            if (path.EndsWith("/vote")) return Json(new OwnVoteResponse(null));
            if (path.EndsWith("/members") || path.EndsWith("/staff")) return Json(new[] { new MemberResponse(Session.User!.UserId, Role ?? "member") });
            throw new InvalidOperationException(path);
        }
        private static Task<HttpResponseMessage> Json<T>(T value, int status = 200) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = JsonContent.Create(value) });
    }
}
