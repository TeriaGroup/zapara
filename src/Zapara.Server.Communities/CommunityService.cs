using Zapara.Contracts.Communities;
using Zapara.Server.Accounts;

namespace Zapara.Server.Communities;

public sealed class CommunityService(IAccountUnitOfWork trustedAccounts, CommunitiesConfiguration configuration)
{
    public Task<IReadOnlyList<CommunityResponse>> ListAsync(string bearer, string? groupId = null, CancellationToken ct = default)
        => Run(bearer, db => groupId is null ? db.ListMembershipsAsync() : db.LookupGroupAsync(CommunityValidation.GroupId(groupId)), ct);
    public Task<CommunityResponse> GetAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.GetAsync(CommunityValidation.Id(communityId)), ct);
    public Task<JoinRequestResponse> RequestJoinAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.RequestJoinAsync(CommunityValidation.Id(communityId)), ct);
    public Task<IReadOnlyList<JoinRequestResponse>> ListJoinRequestsAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.ListJoinRequestsAsync(CommunityValidation.Id(communityId)), ct);
    public Task<JoinRequestResponse> AcceptJoinAsync(string bearer, Guid communityId, Guid requestId, CancellationToken ct = default)
        => Run(bearer, db => db.AcceptJoinAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(requestId)), ct);
    public Task<JoinRequestResponse> RejectJoinAsync(string bearer, Guid communityId, Guid requestId, CancellationToken ct = default)
        => Run(bearer, db => db.RejectJoinAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(requestId)), ct);
    public Task<IReadOnlyList<MemberResponse>> ListMembersAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.ListMembersAsync(CommunityValidation.Id(communityId)), ct);
    public Task<IReadOnlyList<MemberResponse>> ListStaffAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.ListStaffAsync(CommunityValidation.Id(communityId)), ct);
    public Task<HomeworkResponse> PublishHomeworkAsync(string bearer, Guid communityId, HomeworkUpsert request, CancellationToken ct = default)
        => Run(bearer, db => db.PublishHomeworkAsync(CommunityValidation.Id(communityId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<HomeworkResponse> UpdateHomeworkAsync(string bearer, Guid communityId, Guid homeworkId, HomeworkUpsert request, CancellationToken ct = default)
        => Run(bearer, db => db.UpdateHomeworkAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(homeworkId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<IReadOnlyList<HomeworkResponse>> ListHomeworkAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.ListHomeworkAsync(CommunityValidation.Id(communityId)), ct);
    public Task<HomeworkResponse> GetHomeworkAsync(string bearer, Guid communityId, Guid homeworkId, CancellationToken ct = default)
        => Run(bearer, db => db.GetHomeworkAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(homeworkId)), ct);
    public Task<CompletionResponse> UpsertCompletionAsync(string bearer, Guid communityId, Guid homeworkId, CompletionUpsert request, CancellationToken ct = default)
        => Run(bearer, db => db.UpsertCompletionAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(homeworkId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<CompletionResponse> GetCompletionAsync(string bearer, Guid communityId, Guid homeworkId, CancellationToken ct = default)
        => Run(bearer, db => db.GetCompletionAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(homeworkId)), ct);
    public Task<AnnouncementResponse> PublishAnnouncementAsync(string bearer, Guid communityId, AnnouncementUpsert request, CancellationToken ct = default)
        => Run(bearer, db => db.PublishAnnouncementAsync(CommunityValidation.Id(communityId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<AnnouncementResponse> UpdateAnnouncementAsync(string bearer, Guid communityId, Guid announcementId, AnnouncementUpsert request, CancellationToken ct = default)
        => Run(bearer, db => db.UpdateAnnouncementAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(announcementId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<IReadOnlyList<AnnouncementResponse>> ListAnnouncementsAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.ListAnnouncementsAsync(CommunityValidation.Id(communityId)), ct);
    public Task<PollResponse> PublishPollAsync(string bearer, Guid communityId, PollUpsert request, CancellationToken ct = default)
        => Run(bearer, db => db.PublishPollAsync(CommunityValidation.Id(communityId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<IReadOnlyList<PollResponse>> ListPollsAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.ListPollsAsync(CommunityValidation.Id(communityId)), ct);
    public Task<PollResponse> GetPollAsync(string bearer, Guid communityId, Guid pollId, CancellationToken ct = default)
        => Run(bearer, db => db.GetPollAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(pollId)), ct);
    public Task<VoteResponse> VoteAsync(string bearer, Guid communityId, Guid pollId, VoteRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.VoteAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(pollId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<PollResultsResponse> ResultsAsync(string bearer, Guid communityId, Guid pollId, CancellationToken ct = default)
        => Run(bearer, db => db.ResultsAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(pollId)), ct);

    private Task<T> Run<T>(string bearer, Func<CommunityRepository, Task<T>> operation, CancellationToken ct)
        => trustedAccounts.ExecuteAsync(bearer, async (context, token) =>
        {
            try { return await operation(new(context, configuration, token)); }
            catch (ArgumentException) { throw CommunityServiceException.InvalidRequest(); }
        }, ct);
}
