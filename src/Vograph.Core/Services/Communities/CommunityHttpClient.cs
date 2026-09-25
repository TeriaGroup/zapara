using System.Net;
using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Communities;

namespace Vograph.Core.Services.Communities;

/// <summary>Injected clients are trusted: disable redirects, cookies, decompression and default headers.</summary>
public sealed partial class CommunityHttpClient : IDisposable
{
    private readonly HttpClient http;
    private readonly TimeProvider clock;
    private bool ownsHttp;
    public AccountServerScope Scope { get; }

    public CommunityHttpClient(HttpClient http, Uri baseUri, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        Scope = new(baseUri);
        if (http.DefaultRequestHeaders.Any()) throw new ArgumentException("Требуется отдельный HTTP-клиент сообществ.");
        this.http = http;
        this.clock = clock ?? TimeProvider.System;
    }

    public static CommunityHttpClient CreateOwned(Uri baseUri, TimeProvider? clock = null)
    {
        var scope = new AccountServerScope(baseUri);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false, Credentials = null, DefaultProxyCredentials = null, MaxConnectionsPerServer = 4
        };
        return new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, scope.BaseUri, clock) { ownsHttp = true };
    }

    public Task<IReadOnlyList<CommunityResponse>> ListAsync(string accessToken, string? groupId = null, CancellationToken ct = default)
    {
        var path = "";
        if (groupId is not null)
        {
            try { path = "?groupId=" + Uri.EscapeDataString(CommunityValidation.GroupId(groupId)); }
            catch (ArgumentException) { throw new CommunityClientException(CommunityClientFailure.InvalidRequest); }
        }
        return SendList<CommunityResponse>(HttpMethod.Get, path, null, Access(accessToken), 200, ct);
    }

    public Task<CommunityResponse> GetAsync(string accessToken, Guid communityId, CancellationToken ct = default)
        => SendAsync<CommunityResponse>(HttpMethod.Get, "/" + Id(communityId), null, Access(accessToken), 200, ct);
    public Task<JoinRequestResponse> RequestJoinAsync(string accessToken, Guid communityId, CancellationToken ct = default)
        => SendAsync<JoinRequestResponse>(HttpMethod.Post, "/" + Id(communityId) + "/join-requests", null, Access(accessToken), 201, ct);
    public Task<IReadOnlyList<JoinRequestResponse>> ListJoinRequestsAsync(string accessToken, Guid communityId, CancellationToken ct = default)
        => SendList<JoinRequestResponse>(HttpMethod.Get, "/" + Id(communityId) + "/join-requests", null, Access(accessToken), 200, ct);
    public Task<JoinRequestResponse> AcceptJoinAsync(string accessToken, Guid communityId, Guid requestId, CancellationToken ct = default)
        => SendAsync<JoinRequestResponse>(HttpMethod.Post, "/" + Id(communityId) + "/join-requests/" + Id(requestId) + "/accept",
            null, Access(accessToken), 200, ct);
    public Task<JoinRequestResponse> RejectJoinAsync(string accessToken, Guid communityId, Guid requestId, CancellationToken ct = default)
        => SendAsync<JoinRequestResponse>(HttpMethod.Post, "/" + Id(communityId) + "/join-requests/" + Id(requestId) + "/reject",
            null, Access(accessToken), 200, ct);
    public Task<IReadOnlyList<MemberResponse>> ListMembersAsync(string accessToken, Guid communityId, CancellationToken ct = default)
        => SendList<MemberResponse>(HttpMethod.Get, "/" + Id(communityId) + "/members", null, Access(accessToken), 200, ct);
    public Task<IReadOnlyList<MemberResponse>> ListStaffAsync(string accessToken, Guid communityId, CancellationToken ct = default)
        => SendList<MemberResponse>(HttpMethod.Get, "/" + Id(communityId) + "/staff", null, Access(accessToken), 200, ct);
    public Task<GroupDeskResponse> DeskAsync(string accessToken, Guid communityId, CancellationToken ct = default)
        => SendAsync<GroupDeskResponse>(HttpMethod.Get, "/" + Id(communityId) + "/desk", null, Access(accessToken), 200, ct);
    public Task<GroupDeskResponse> CreateRoleAsync(string accessToken, Guid communityId, GroupRoleNameRequest request, CancellationToken ct = default)
        => SendAsync<GroupDeskResponse>(HttpMethod.Post, "/" + Id(communityId) + "/roles", Required(request), Access(accessToken), 201, ct);
    public Task<GroupDeskResponse> SetRolePowerAsync(string accessToken, Guid communityId, Guid roleId, GroupPowerRequest request, CancellationToken ct = default)
        => SendAsync<GroupDeskResponse>(HttpMethod.Post, "/" + Id(communityId) + "/roles/" + Id(roleId) + "/powers", Required(request), Access(accessToken), 200, ct);
    public Task<GroupDeskResponse> GrantRoleAsync(string accessToken, Guid communityId, Guid roleId, GroupGrantRequest request, CancellationToken ct = default)
        => SendAsync<GroupDeskResponse>(HttpMethod.Post, "/" + Id(communityId) + "/roles/" + Id(roleId) + "/grants", Required(request), Access(accessToken), 200, ct);
    public Task<GroupDeskResponse> RevokeRoleAsync(string accessToken, Guid communityId, Guid roleId, Guid userId, CancellationToken ct = default)
        => SendAsync<GroupDeskResponse>(HttpMethod.Post, "/" + Id(communityId) + "/roles/" + Id(roleId) + "/grants/" + Id(userId) + "/delete", null, Access(accessToken), 200, ct);
    public Task<IReadOnlyList<HomeworkResponse>> ListHomeworkAsync(string accessToken, Guid communityId, CancellationToken ct = default)
        => SendList<HomeworkResponse>(HttpMethod.Get, "/" + Id(communityId) + "/homework", null, Access(accessToken), 200, ct);
    public Task<HomeworkResponse> PublishHomeworkAsync(string accessToken, Guid communityId, HomeworkUpsert request, CancellationToken ct = default)
        => SendAsync<HomeworkResponse>(HttpMethod.Post, "/" + Id(communityId) + "/homework", Required(request), Access(accessToken), 201, ct);
    public Task<HomeworkResponse> ShareHomeworkAsync(string accessToken, Guid communityId, HomeworkUpsert request, CancellationToken ct = default)
        => SendAsync<HomeworkResponse>(HttpMethod.Post, "/" + Id(communityId) + "/homework/share", Required(request), Access(accessToken), 201, ct);
    public Task<HomeworkResponse> GetHomeworkAsync(string accessToken, Guid communityId, Guid homeworkId, CancellationToken ct = default)
        => SendAsync<HomeworkResponse>(HttpMethod.Get, "/" + Id(communityId) + "/homework/" + Id(homeworkId), null, Access(accessToken), 200, ct);
    public Task<HomeworkResponse> UpdateHomeworkAsync(string accessToken, Guid communityId, Guid homeworkId, HomeworkUpsert request, CancellationToken ct = default)
        => SendAsync<HomeworkResponse>(HttpMethod.Put, "/" + Id(communityId) + "/homework/" + Id(homeworkId), Required(request), Access(accessToken), 200, ct);
    public Task<CompletionResponse> GetCompletionAsync(string accessToken, Guid communityId, Guid homeworkId, CancellationToken ct = default)
        => SendAsync<CompletionResponse>(HttpMethod.Get, "/" + Id(communityId) + "/homework/" + Id(homeworkId) + "/completion",
            null, Access(accessToken), 200, ct);
    public Task<CompletionResponse> UpsertCompletionAsync(string accessToken, Guid communityId, Guid homeworkId, CompletionUpsert request, CancellationToken ct = default)
        => SendAsync<CompletionResponse>(HttpMethod.Put, "/" + Id(communityId) + "/homework/" + Id(homeworkId) + "/completion",
            Required(request), Access(accessToken), 200, ct);
    public Task<IReadOnlyList<AnnouncementResponse>> ListAnnouncementsAsync(string accessToken, Guid communityId, CancellationToken ct = default)
        => SendList<AnnouncementResponse>(HttpMethod.Get, "/" + Id(communityId) + "/announcements", null, Access(accessToken), 200, ct);
    public Task<AnnouncementResponse> PublishAnnouncementAsync(string accessToken, Guid communityId, AnnouncementUpsert request, CancellationToken ct = default)
        => SendAsync<AnnouncementResponse>(HttpMethod.Post, "/" + Id(communityId) + "/announcements", Required(request), Access(accessToken), 201, ct);
    public Task<AnnouncementResponse> UpdateAnnouncementAsync(string accessToken, Guid communityId, Guid announcementId, AnnouncementUpsert request, CancellationToken ct = default)
        => SendAsync<AnnouncementResponse>(HttpMethod.Put, "/" + Id(communityId) + "/announcements/" + Id(announcementId),
            Required(request), Access(accessToken), 200, ct);
    public Task<IReadOnlyList<PollResponse>> ListPollsAsync(string accessToken, Guid communityId, CancellationToken ct = default)
        => SendList<PollResponse>(HttpMethod.Get, "/" + Id(communityId) + "/polls", null, Access(accessToken), 200, ct);
    public Task<PollResponse> PublishPollAsync(string accessToken, Guid communityId, PollUpsert request, CancellationToken ct = default)
        => SendAsync<PollResponse>(HttpMethod.Post, "/" + Id(communityId) + "/polls", Required(request), Access(accessToken), 201, ct);
    public Task<PollResponse> GetPollAsync(string accessToken, Guid communityId, Guid pollId, CancellationToken ct = default)
        => SendAsync<PollResponse>(HttpMethod.Get, "/" + Id(communityId) + "/polls/" + Id(pollId), null, Access(accessToken), 200, ct);
    public Task<VoteResponse> VoteAsync(string accessToken, Guid communityId, Guid pollId, VoteRequest request, CancellationToken ct = default)
        => SendAsync<VoteResponse>(HttpMethod.Post, "/" + Id(communityId) + "/polls/" + Id(pollId) + "/votes", Required(request), Access(accessToken), 201, ct);
    public Task<PollResultsResponse> ResultsAsync(string accessToken, Guid communityId, Guid pollId, CancellationToken ct = default)
        => SendAsync<PollResultsResponse>(HttpMethod.Get, "/" + Id(communityId) + "/polls/" + Id(pollId) + "/results",
            null, Access(accessToken), 200, ct);
    public Task<GroupTopicListResponse> TopicsAsync(string accessToken, Guid communityId, CancellationToken ct = default)
        => SendAsync<GroupTopicListResponse>(HttpMethod.Get, "/" + Id(communityId) + "/topics?typed=1", null, Access(accessToken), 200, ct);
    public Task<GroupTopicListResponse> CreateTopicAsync(string accessToken, Guid communityId, GroupTopicRequest request, CancellationToken ct = default)
        => SendAsync<GroupTopicListResponse>(HttpMethod.Post, "/" + Id(communityId) + "/topics?typed=1", Required(request), Access(accessToken), 201, ct);
    public Task<GroupTopicListResponse> RenameTopicAsync(string accessToken, Guid communityId, Guid topicId, GroupTopicRequest request, CancellationToken ct = default)
        => SendAsync<GroupTopicListResponse>(HttpMethod.Post, "/" + Id(communityId) + "/topics/" + Id(topicId) + "?typed=1", Required(request), Access(accessToken), 200, ct);
    public Task<GroupTopicListResponse> DeleteTopicAsync(string accessToken, Guid communityId, Guid topicId, CancellationToken ct = default)
        => SendAsync<GroupTopicListResponse>(HttpMethod.Post, "/" + Id(communityId) + "/topics/" + Id(topicId) + "/delete?typed=1", null, Access(accessToken), 200, ct);
    public Task<BallotBoardResponse> BallotsAsync(string accessToken, Guid communityId, Guid? topicId = null, CancellationToken ct = default)
        => SendAsync<BallotBoardResponse>(HttpMethod.Get, "/" + Id(communityId) + "/ballots" + (topicId is Guid topic ? "?topic=" + Id(topic) : ""), null, Access(accessToken), 200, ct);
    public Task<BallotBoardResponse> OpenHeadmanBallotAsync(string accessToken, Guid communityId, BallotDraftRequest request, CancellationToken ct = default)
        => SendAsync<BallotBoardResponse>(HttpMethod.Post, "/" + Id(communityId) + "/ballots/headman", Required(request), Access(accessToken), 201, ct);
    public Task<BallotBoardResponse> ProposeBallotAsync(string accessToken, Guid communityId, BallotDraftRequest request, CancellationToken ct = default)
        => SendAsync<BallotBoardResponse>(HttpMethod.Post, "/" + Id(communityId) + "/ballots/collective", Required(request), Access(accessToken), 201, ct);
    public Task<BallotBoardResponse> SupportBallotAsync(string accessToken, Guid communityId, Guid ballotId, CancellationToken ct = default)
        => SendAsync<BallotBoardResponse>(HttpMethod.Post, "/" + Id(communityId) + "/ballots/" + Id(ballotId) + "/support", null, Access(accessToken), 200, ct);
    public Task<BallotBoardResponse> VoteBallotAsync(string accessToken, Guid communityId, Guid ballotId, VoteRequest request, CancellationToken ct = default)
        => SendAsync<BallotBoardResponse>(HttpMethod.Post, "/" + Id(communityId) + "/ballots/" + Id(ballotId) + "/votes", Required(request), Access(accessToken), 200, ct);
    public Task<BallotBoardResponse> CloseBallotAsync(string accessToken, Guid communityId, Guid ballotId, CancellationToken ct = default)
        => SendAsync<BallotBoardResponse>(HttpMethod.Post, "/" + Id(communityId) + "/ballots/" + Id(ballotId) + "/close", null, Access(accessToken), 200, ct);
    public Task<GroupHomeResponse> GroupHomeAsync(string accessToken, Guid communityId, CancellationToken ct = default)
        => SendAsync<GroupHomeResponse>(HttpMethod.Get, "/" + Id(communityId) + "/home", null, Access(accessToken), 200, ct);
    public Task<ConversationResponse> OpenDirectAsync(string accessToken, OpenDirectRequest request, CancellationToken ct = default)
        => SendAsync<ConversationResponse>(HttpMethod.Post, "/direct", Required(request), Access(accessToken), 201, ct);
    public Task<ChatPageResponse> MessagesAsync(string accessToken, Guid conversationId, Guid? before = null, Guid? after = null, CancellationToken ct = default, string? topic = null)
    {
        var path = "/conversations/" + Id(conversationId) + "/messages";
        if (topic is not null) path += "?topic=" + Topic(topic);
        if (before is Guid older) path += (topic is null ? "?" : "&") + "before=" + Id(older);
        else if (after is Guid newer) path += (topic is null ? "?" : "&") + "after=" + Id(newer);
        return SendAsync<ChatPageResponse>(HttpMethod.Get, path, null, Access(accessToken), 200, ct);
    }
    public Task<ChatMessageResponse> SendTopicMessageAsync(string accessToken, Guid conversationId, TopicMessageRequest request, CancellationToken ct = default)
        => SendAsync<ChatMessageResponse>(HttpMethod.Post, "/conversations/" + Id(conversationId) + "/topic-messages", Required(request), Access(accessToken), 201, ct);
    public Task<ChatMessageResponse> SendMessageAsync(string accessToken, Guid conversationId, SendMessageRequest request, CancellationToken ct = default)
        => SendAsync<ChatMessageResponse>(HttpMethod.Post, "/conversations/" + Id(conversationId) + "/messages", Required(request), Access(accessToken), 201, ct);
    public Task<ChatMessageResponse> SendMediaAsync(string accessToken, Guid conversationId, string kind, string name, byte[] bytes, Guid? replyTo = null, CancellationToken ct = default, int? durationMs = null, Guid? topicId = null)
        => SendMediaCoreAsync(Access(accessToken), Id(conversationId), kind, name, bytes, replyTo, ct, durationMs, topicId);
    public Task<ChatMessageResponse> EditMessageAsync(string accessToken, Guid conversationId, Guid messageId, SendMessageRequest request, CancellationToken ct = default)
        => SendAsync<ChatMessageResponse>(HttpMethod.Post, "/conversations/" + Id(conversationId) + "/messages/" + Id(messageId) + "/edit", Required(request), Access(accessToken), 200, ct);
    public Task<ChatMessageResponse> DeleteMessageAsync(string accessToken, Guid conversationId, Guid messageId, CancellationToken ct = default)
        => SendAsync<ChatMessageResponse>(HttpMethod.Post, "/conversations/" + Id(conversationId) + "/messages/" + Id(messageId) + "/delete", null, Access(accessToken), 200, ct);
    public Task<ChatMessageResponse> ReactMessageAsync(string accessToken, Guid conversationId, Guid messageId, string emoji, CancellationToken ct = default)
        => SendAsync<ChatMessageResponse>(HttpMethod.Post, "/conversations/" + Id(conversationId) + "/messages/" + Id(messageId) + "/react", new ReactMessageRequest(emoji), Access(accessToken), 200, ct);
    public Task<ConversationResponse> MarkReadAsync(string accessToken, Guid conversationId, CancellationToken ct = default)
        => SendAsync<ConversationResponse>(HttpMethod.Post, "/conversations/" + Id(conversationId) + "/read", null, Access(accessToken), 200, ct);

    private async Task<IReadOnlyList<T>> SendList<T>(HttpMethod method, string path, object? body, string access, int status, CancellationToken ct)
        => await SendAsync<T[]>(method, path, body, access, status, ct).ConfigureAwait(false);
    private static string Access(string value) => AccountValidation.Token(value, "za_");
    private static string Id(Guid value) => CommunityValidation.Id(value).ToString("D");
    private static string Topic(string value) => value == "general" ? value
        : Guid.TryParseExact(value, "D", out var id) ? Id(id) : throw new CommunityClientException(CommunityClientFailure.InvalidRequest);
    private static T Required<T>(T request) where T : class => request ?? throw new CommunityClientException(CommunityClientFailure.InvalidRequest);
    public void Dispose() { if (ownsHttp) http.Dispose(); }
}
