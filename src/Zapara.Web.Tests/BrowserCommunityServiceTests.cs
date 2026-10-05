using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Communities;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserCommunityServiceTests
{
    [Fact]
    public async Task Accepting_a_request_refreshes_members_without_guessing_their_role()
    {
        using var h = await Harness.Create();
        var applicant = Guid.NewGuid();
        h.Backend.PendingRequest = new(Guid.NewGuid(), h.Backend.CommunityId, applicant, "pending", DateTimeOffset.UtcNow);
        await h.Service.OpenAsync(h.Backend.CommunityId);
        var request = Assert.Single(h.Service.JoinRequests);
        Assert.True(await h.Service.ResolveJoinAsync(h.Backend.CommunityId, request.RequestId, true, h.Family));
        Assert.Empty(h.Service.JoinRequests);
        Assert.Contains(h.Service.Members, m => m.UserId == applicant && m.Role == "curator");
    }

    [Fact]
    public async Task Delayed_authorized_read_is_discarded_after_switching_accounts()
    {
        using var h = await Harness.Create();
        h.Backend.Paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Backend.Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var loading = h.Service.OpenAsync(h.Backend.CommunityId);
        await h.Backend.Paused.Task.WaitAsync(TestContext.Current.CancellationToken);
        h.Backend.Session = NewSession();
        await h.Api.RefreshSessionAsync(TestContext.Current.CancellationToken);
        h.Backend.Resume.SetResult();
        await loading;
        Assert.Null(h.Service.Selected);
        Assert.Empty(h.Service.Homework);
        Assert.Empty(h.Service.Members);
        Assert.Empty(h.Service.OwnVotes);
    }

    [Fact]
    public async Task Applicant_reloads_pending_then_rejected_status_without_staff_access()
    {
        using var h = await Harness.Create();
        h.Backend.Role = null;
        h.Backend.OwnRequest = new(Guid.NewGuid(), h.Backend.CommunityId, h.Backend.Session.User!.UserId, "pending", DateTimeOffset.UtcNow);
        await h.Service.LoadAsync("g");
        Assert.Equal("pending", h.Service.OwnRequests[h.Backend.CommunityId]!.Status);
        h.Backend.OwnRequest = new(h.Backend.OwnRequest.RequestId, h.Backend.CommunityId, h.Backend.Session.User.UserId, "rejected", DateTimeOffset.UtcNow);
        await h.Service.OpenAsync(h.Backend.CommunityId);
        Assert.Equal("rejected", h.Service.OwnRequests[h.Backend.CommunityId]!.Status);
        Assert.False(h.Service.IsMember);
        Assert.Empty(h.Service.JoinRequests);
    }

    [Fact]
    public async Task Revoked_staff_response_clears_protected_data_and_blocks_cached_role_writes()
    {
        using var h = await Harness.Create();
        await h.Service.OpenAsync(h.Backend.CommunityId);
        Assert.True(h.Service.CanPublish);
        Assert.Single(h.Service.Homework);
        h.Backend.Role = "member";
        Assert.False(await h.Service.SaveHomeworkAsync(h.Backend.CommunityId, h.Family, "Новое", "Текст"));
        Assert.False(h.Service.CanPublish);
        Assert.Empty(h.Service.Homework);
        Assert.False(await h.Service.SaveHomeworkAsync(h.Backend.CommunityId, h.Family, "Повтор", "Текст"));
        Assert.Equal(1, h.Backend.Mutations);
        await h.Service.OpenAsync(h.Backend.CommunityId);
        Assert.True(h.Service.IsMember);
        Assert.False(h.Service.IsStaff);
    }

    [Fact]
    public async Task Old_editor_family_cannot_submit_as_new_account_even_in_the_same_community()
    {
        using var h = await Harness.Create();
        await h.Service.OpenAsync(h.Backend.CommunityId);
        var previous = h.Family;
        h.Backend.Session = NewSession();
        await h.Api.RefreshSessionAsync(TestContext.Current.CancellationToken);
        Assert.Empty(h.Service.Homework);
        Assert.Null(h.Service.Selected);
        await h.Service.OpenAsync(h.Backend.CommunityId);
        Assert.False(await h.Service.SaveHomeworkAsync(h.Backend.CommunityId, previous, "Старый черновик", "Не отправлять"));
        Assert.Equal(0, h.Backend.Mutations);
    }

    [Fact]
    public async Task Own_vote_reload_disables_second_vote_and_expired_poll_never_sends_a_mutation()
    {
        using var h = await Harness.Create();
        h.Backend.OwnVote = new(h.Backend.PollId, h.Backend.OptionId, DateTimeOffset.UtcNow);
        await h.Service.OpenAsync(h.Backend.CommunityId);
        Assert.False(h.Service.CanVote(h.Service.Polls.Single()));
        Assert.False(await h.Service.VoteAsync(h.Backend.CommunityId, h.Family, h.Service.Polls.Single(), h.Backend.OptionId));
        Assert.Equal(0, h.Backend.Mutations);
        h.Backend.OwnVote = null;
        h.Backend.Deadline = DateTimeOffset.UtcNow.AddMinutes(-1);
        await h.Service.OpenAsync(h.Backend.CommunityId);
        Assert.False(await h.Service.VoteAsync(h.Backend.CommunityId, h.Family, h.Service.Polls.Single(), h.Backend.OptionId));
        Assert.Equal(0, h.Backend.Mutations);
    }

    [Fact]
    public async Task Unknown_vote_outcome_requires_reload_and_is_never_queued_or_automatically_retried()
    {
        using var h = await Harness.Create();
        await h.Service.OpenAsync(h.Backend.CommunityId);
        h.Backend.OfflineVotes = true;
        Assert.False(await h.Service.VoteAsync(h.Backend.CommunityId, h.Family, h.Service.Polls.Single(), h.Backend.OptionId));
        Assert.True(h.Service.RequiresRefresh);
        Assert.False(await h.Service.VoteAsync(h.Backend.CommunityId, h.Family, h.Service.Polls.Single(), h.Backend.OptionId));
        Assert.Equal(1, h.Backend.Mutations);
        Assert.Null(h.Service.OwnVotes[h.Backend.PollId]);
    }

    [Fact]
    public async Task Staff_publication_preserves_multiline_and_members_personal_completion_is_separate()
    {
        using var h = await Harness.Create();
        await h.Service.OpenAsync(h.Backend.CommunityId);
        Assert.True(await h.Service.SaveHomeworkAsync(h.Backend.CommunityId, h.Family, "Тема", "Первый\r\nВторой"));
        var homework = h.Service.Homework.Single(x => x.Title == "Тема");
        Assert.Equal("Первый\nВторой", homework.Body);
        Assert.False(h.Service.Completions[homework.HomeworkId].Completed);
        Assert.True(await h.Service.CompleteAsync(h.Backend.CommunityId, h.Family, homework, true));
        Assert.True(h.Service.Completions[homework.HomeworkId].Completed);
        Assert.False(h.Service.Completions[h.Backend.HomeworkId].Completed);
    }

    private static BrowserSession NewSession() => new(true, new UserResponse(Guid.NewGuid(), "test.user", "Участник", DateTimeOffset.UtcNow), Guid.NewGuid(), new string('a', 43), new());
    private sealed class Harness : IDisposable
    {
        public Backend Backend { get; } = new();
        private HttpClient http = null!;
        private BrowserStorage storage = null!;
        public BrowserApiClient Api { get; private set; } = null!;
        public BrowserCommunityService Service { get; private set; } = null!;
        public Guid Family => Api.Session.FamilyId!.Value;
        public static async Task<Harness> Create()
        {
            var h = new Harness();
            h.http = new(h.Backend) { BaseAddress = new("https://zapara.test/app/") };
            h.storage = new(new MemoryBrowser());
            h.Api = new(h.http, h.storage);
            var state = new WebAppState(h.http, h.storage, h.Api);
            await state.InitializeAsync();
            h.Service = new(h.Api, h.storage, state);
            await h.Service.LoadAsync("g");
            return h;
        }
        public void Dispose() { Service.Dispose(); http.Dispose(); storage.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private sealed class Backend : HttpMessageHandler
    {
        public BrowserSession Session = NewSession();
        public Guid CommunityId = Guid.NewGuid(), HomeworkId = Guid.NewGuid(), PollId = Guid.NewGuid(), OptionId = Guid.NewGuid();
        public string? Role = "headman";
        public DateTimeOffset Deadline = DateTimeOffset.UtcNow.AddDays(1);
        public VoteResponse? OwnVote;
        public JoinRequestResponse? OwnRequest;
        public JoinRequestResponse? PendingRequest;
        public Guid? AcceptedMember;
        public TaskCompletionSource? Paused, Resume;
        public bool OfflineVotes;
        public int Mutations;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (!path.StartsWith("/web-api/", StringComparison.Ordinal)) return new(HttpStatusCode.ServiceUnavailable);
            if (path == "/web-api/session") return Json(Session);
            var community = new CommunityResponse(CommunityId, "Учебная группа", "Сообщество", 1, Role);
            if (request.Method == HttpMethod.Get)
            {
                if (path == "/web-api/communities") return Json(new[] { community });
                if (path == $"/web-api/communities/{CommunityId:D}") return Json(community);
                if (path.EndsWith("/join-request")) return Json(new OwnJoinRequestResponse(OwnRequest));
                if (path.EndsWith("/join-requests")) return Json(PendingRequest is null ? Array.Empty<JoinRequestResponse>() : new[] { PendingRequest });
                if (path.EndsWith("/homework")) return Json(new[] { new HomeworkResponse(HomeworkId, CommunityId, "Первое", "Текст", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) });
                if (path.EndsWith("/completion")) return Json(new CompletionResponse(HomeworkId, false, 0, null));
                if (path.EndsWith("/announcements")) { if (Paused is not null) { Paused.TrySetResult(); await Resume!.Task; } return Json(Array.Empty<AnnouncementResponse>()); }
                if (path.EndsWith("/polls")) return Json(new[] { new PollResponse(PollId, CommunityId, "Выберите", Deadline, 1, [new(OptionId, "Первый", 1), new(Guid.NewGuid(), "Второй", 2)]) });
                if (path.EndsWith("/vote")) return Json(new OwnVoteResponse(OwnVote));
                if (path.EndsWith("/members") || path.EndsWith("/staff")) return Json(AcceptedMember is { } accepted ? new[] { new MemberResponse(Session.User!.UserId, Role ?? "member"), new MemberResponse(accepted, "curator") } : new[] { new MemberResponse(Session.User!.UserId, Role ?? "member") });
            }
            Mutations++;
            if (path.EndsWith("/accept"))
            {
                var current = PendingRequest!;
                AcceptedMember = current.UserId; PendingRequest = null;
                return Json(new JoinRequestResponse(current.RequestId, current.CommunityId, current.UserId, "accepted", current.CreatedAt));
            }
            if (path.EndsWith("/votes") && OfflineVotes) throw new HttpRequestException("offline");
            if (Role != "headman" && path.EndsWith("/homework")) return new(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new CommunityError("Доступ запрещён", 403, "forbidden")) };
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (path.EndsWith("/homework")) return Json(new HomeworkResponse(Guid.NewGuid(), CommunityId, body.RootElement.GetProperty("title").GetString()!, body.RootElement.GetProperty("body").GetString()!, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), 201);
            if (path.EndsWith("/completion")) return Json(new CompletionResponse(Guid.Parse(path.Split('/')[^2]), body.RootElement.GetProperty("completed").GetBoolean(), 1, DateTimeOffset.UtcNow));
            throw new InvalidOperationException("Unexpected request: " + path);
        }
        private static HttpResponseMessage Json<T>(T value, int status = 200) => new((HttpStatusCode)status) { Content = JsonContent.Create(value) };
    }
}
