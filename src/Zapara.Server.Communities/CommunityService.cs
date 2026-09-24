using System.Text;
using Zapara.Contracts.Communities;
using Zapara.Server.Accounts;

namespace Zapara.Server.Communities;

public sealed class CommunityService(IAccountUnitOfWork trustedAccounts, CommunitiesConfiguration configuration, IContentArchive? archive = null, IUploadQuota? uploads = null)
{
    public Task<IReadOnlyList<CommunityResponse>> ListAsync(string bearer, string? groupId = null, CancellationToken ct = default)
        => Run(bearer, db => groupId is null ? db.ListMembershipsAsync() : db.LookupGroupAsync(CommunityValidation.GroupId(groupId)), ct);
    public Task<CommunityResponse> GetAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.GetAsync(CommunityValidation.Id(communityId)), ct);
    public Task<JoinRequestResponse> RequestJoinAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.RequestJoinAsync(CommunityValidation.Id(communityId)), ct);
    public Task<OwnJoinRequestResponse> GetOwnJoinRequestAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.GetOwnJoinRequestAsync(CommunityValidation.Id(communityId)), ct);
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
    public Task<GroupDeskResponse> DeskAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.DeskAsync(CommunityValidation.Id(communityId)), ct);
    public Task<GroupDeskResponse> CreateRoleAsync(string bearer, Guid communityId, GroupRoleNameRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.CreateRoleAsync(CommunityValidation.Id(communityId), request?.Name), ct);
    public Task<GroupDeskResponse> RenameRoleAsync(string bearer, Guid communityId, Guid roleId, GroupRoleNameRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.RenameRoleAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(roleId), request?.Name), ct);
    public Task<GroupDeskResponse> DeleteRoleAsync(string bearer, Guid communityId, Guid roleId, CancellationToken ct = default)
        => Run(bearer, db => db.DeleteRoleAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(roleId)), ct);
    public Task<GroupDeskResponse> GrantRoleAsync(string bearer, Guid communityId, Guid roleId, GroupGrantRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.GrantRoleAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(roleId), request is null ? Guid.Empty : CommunityValidation.Id(request.UserId)), ct);
    public Task<GroupDeskResponse> RevokeRoleAsync(string bearer, Guid communityId, Guid roleId, Guid userId, CancellationToken ct = default)
        => Run(bearer, db => db.RevokeRoleAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(roleId), CommunityValidation.Id(userId)), ct);
    public Task<GroupDeskResponse> RemoveMemberAsync(string bearer, Guid communityId, Guid userId, CancellationToken ct = default)
        => Run(bearer, db => db.RemoveMemberAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(userId)), ct);
    public Task<GroupDeskResponse> SetRolePowerAsync(string bearer, Guid communityId, Guid roleId, GroupPowerRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.SetRolePowerAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(roleId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<BallotBoardResponse> BallotsAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.BoardAsync(CommunityValidation.Id(communityId)), ct);
    public Task<BallotBoardResponse> OpenHeadmanBallotAsync(string bearer, Guid communityId, BallotDraftRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.OpenHeadmanBallotAsync(CommunityValidation.Id(communityId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<BallotBoardResponse> ProposeBallotAsync(string bearer, Guid communityId, BallotDraftRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.ProposeBallotAsync(CommunityValidation.Id(communityId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<BallotBoardResponse> SupportBallotAsync(string bearer, Guid communityId, Guid ballotId, CancellationToken ct = default)
        => Run(bearer, db => db.SupportBallotAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(ballotId)), ct);
    public Task<BallotBoardResponse> VoteBallotAsync(string bearer, Guid communityId, Guid ballotId, VoteRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.VoteBallotAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(ballotId), (request ?? throw CommunityServiceException.InvalidRequest()).OptionId), ct);
    public Task<BallotBoardResponse> CloseBallotAsync(string bearer, Guid communityId, Guid ballotId, CancellationToken ct = default)
        => Run(bearer, db => db.CloseBallotAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(ballotId)), ct);
    public Task<BallotBoardResponse> ProposeChangeAsync(string bearer, Guid communityId, BallotChangeRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.ProposeChangeAsync(CommunityValidation.Id(communityId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public async Task<HomeworkResponse> PublishHomeworkAsync(string bearer, Guid communityId, HomeworkUpsert request, CancellationToken ct = default)
        => StoreHomework(await Run(bearer, db => db.PublishHomeworkAsync(CommunityValidation.Id(communityId), request ?? throw CommunityServiceException.InvalidRequest()), ct));
    public async Task<HomeworkResponse> ShareHomeworkAsync(string bearer, Guid communityId, HomeworkUpsert request, CancellationToken ct = default)
        => StoreHomework(await Run(bearer, db => db.ShareHomeworkAsync(CommunityValidation.Id(communityId), request ?? throw CommunityServiceException.InvalidRequest()), ct));
    public async Task<IReadOnlyList<GroupHomeworkCopyResponse>> ListHomeworkCopiesAsync(string bearer, Guid communityId, CancellationToken ct = default)
    {
        var list = await Run(bearer, db => db.ListHomeworkCopiesAsync(CommunityValidation.Id(communityId)), ct);
        return list.Select(item =>
        {
            var body = ReadText(ContentNames.Homework(item.HomeworkId), item.Body);
            return body == item.Body ? item : new GroupHomeworkCopyResponse(item.HomeworkId, item.Title, body, item.Revision, item.Completed, item.CompletionRevision);
        }).ToArray();
    }
    public async Task<HomeworkResponse> UpdateHomeworkAsync(string bearer, Guid communityId, Guid homeworkId, HomeworkUpsert request, CancellationToken ct = default)
        => StoreHomework(await Run(bearer, db => db.UpdateHomeworkAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(homeworkId), request ?? throw CommunityServiceException.InvalidRequest()), ct));
    public async Task<IReadOnlyList<HomeworkResponse>> ListHomeworkAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => (await Run(bearer, db => db.ListHomeworkAsync(CommunityValidation.Id(communityId)), ct)).Select(LoadHomework).ToArray();
    public async Task<HomeworkResponse> GetHomeworkAsync(string bearer, Guid communityId, Guid homeworkId, CancellationToken ct = default)
        => LoadHomework(await Run(bearer, db => db.GetHomeworkAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(homeworkId)), ct));
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
    public Task<OwnVoteResponse> GetOwnVoteAsync(string bearer, Guid communityId, Guid pollId, CancellationToken ct = default)
        => Run(bearer, db => db.GetOwnVoteAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(pollId)), ct);
    public Task<PollResultsResponse> ResultsAsync(string bearer, Guid communityId, Guid pollId, CancellationToken ct = default)
        => Run(bearer, db => db.ResultsAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(pollId)), ct);
    public Task<GroupHomeResponse> GroupHomeAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.GroupHomeAsync(CommunityValidation.Id(communityId)), ct);
    public Task<ConversationResponse> OpenDirectAsync(string bearer, OpenDirectRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.OpenDirectAsync(CommunityValidation.Id(request.CommunityId), CommunityValidation.Id(request.UserId)), ct);
    public async Task<ChatPageResponse> ListMessagesAsync(string bearer, Guid conversationId, Guid? before, Guid? after, string? topic = null, CancellationToken ct = default)
    {
        var page = await Run(bearer, db => db.ListMessagesAsync(CommunityValidation.Id(conversationId),
            before is null ? null : CommunityValidation.Id(before.Value), after is null ? null : CommunityValidation.Id(after.Value), topic), ct);
        return new(page.Messages.Select(LoadMessage).ToArray(), page.HasMore);
    }
    public async Task<ChatMessageResponse> SendMessageAsync(string bearer, Guid conversationId, SendMessageRequest request, CancellationToken ct = default)
        => StoreMessage(await Run(bearer, db => db.SendMessageAsync(CommunityValidation.Id(conversationId), request.Body, request.ReplyTo, request.Kind), ct));
    public async Task<ChatMessageResponse> SendMediaAsync(string bearer, Guid conversationId, string kind, string name, byte[] content, Guid? replyTo, int? durationMs = null, CancellationToken ct = default)
    {
        if (kind is not ("image" or "video" or "file" or "voice" or "circle")) throw CommunityServiceException.InvalidRequest();
        if (content is null || content.Length is < 1 or > CommunityMedia.MaxBytes ||
            kind == "voice" && content.Length > CommunityMedia.MaxVoiceBytes)
            throw new CommunityServiceException(413, "payload_too_large");
        CommunityMedia.ValidateRecording(kind, content, durationMs);
        var label = MediaName(kind, name);
        var message = StoreMessage(await Run(bearer, db => db.SendMessageAsync(CommunityValidation.Id(conversationId), label, replyTo, kind), ct));
        var key = ContentNames.GroupFile(message.MessageId);
        try
        {
            if (uploads is not null) await uploads.Accept(trustedAccounts, bearer, null, key, content, ct);
            else if (archive is not null) archive.Put(key, content);
            else throw new CommunityServiceException(503, "storage_unavailable");
        }
        catch (Exception)
        {
            try { await Run(bearer, db => db.DeleteMessageAsync(message.ConversationId, message.MessageId), ct); }
            catch (CommunityServiceException) { }
            Drop(message.MessageId);
            throw;
        }
        return message;
    }
    public async Task<byte[]> ReadMediaAsync(string bearer, Guid conversationId, Guid messageId, CancellationToken ct = default)
    {
        await Run(bearer, db => db.OpenMediaAsync(CommunityValidation.Id(conversationId), CommunityValidation.Id(messageId)), ct);
        var stored = archive?.Get(ContentNames.GroupFile(messageId));
        if (stored is null || stored.Length == 0) throw CommunityServiceException.NotFound();
        return stored;
    }
    public async Task<ChatMessageResponse> EditMessageAsync(string bearer, Guid conversationId, Guid messageId, SendMessageRequest request, CancellationToken ct = default)
        => StoreMessage(await Run(bearer, db => db.EditMessageAsync(CommunityValidation.Id(conversationId), CommunityValidation.Id(messageId), request.Body), ct));
    public async Task<ChatMessageResponse> DeleteMessageAsync(string bearer, Guid conversationId, Guid messageId, CancellationToken ct = default)
    {
        var message = await Run(bearer, db => db.DeleteMessageAsync(CommunityValidation.Id(conversationId), CommunityValidation.Id(messageId)), ct);
        Drop(message.MessageId);
        return message;
    }
    public async Task<ChatMessageResponse> ReactMessageAsync(string bearer, Guid conversationId, Guid messageId, ReactMessageRequest request, CancellationToken ct = default)
        => await Run(bearer, db => db.ReactMessageAsync(CommunityValidation.Id(conversationId), CommunityValidation.Id(messageId), request.Emoji), ct);
    public async Task<ChatMessageResponse> SendTopicMessageAsync(string bearer, Guid conversationId, TopicMessageRequest request, CancellationToken ct = default)
        => StoreMessage(await Run(bearer, db => db.SendTopicMessageAsync(CommunityValidation.Id(conversationId), request.Body, request?.TopicId, request?.ReplyTo), ct));
    public Task<GroupTopicListResponse> TopicsAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.TopicsAsync(CommunityValidation.Id(communityId)), ct);
    public Task<GroupTopicListResponse> CreateTopicAsync(string bearer, Guid communityId, GroupTopicRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.CreateTopicAsync(CommunityValidation.Id(communityId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<GroupTopicListResponse> RenameTopicAsync(string bearer, Guid communityId, Guid topicId, GroupTopicRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.RenameTopicAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(topicId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<GroupTopicListResponse> DeleteTopicAsync(string bearer, Guid communityId, Guid topicId, CancellationToken ct = default)
        => Run(bearer, db => db.DeleteTopicAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(topicId)), ct);
    public Task<ConversationResponse> MarkReadAsync(string bearer, Guid conversationId, CancellationToken ct = default)
        => Run(bearer, db => db.MarkReadAsync(CommunityValidation.Id(conversationId)), ct);

    private HomeworkResponse StoreHomework(HomeworkResponse item)
    {
        if (archive is null) return item;
        archive.Put(ContentNames.Homework(item.HomeworkId), Encoding.UTF8.GetBytes(item.Body));
        return LoadHomework(item);
    }

    private HomeworkResponse LoadHomework(HomeworkResponse item)
    {
        var body = ReadText(ContentNames.Homework(item.HomeworkId), item.Body);
        return body == item.Body ? item : new HomeworkResponse(item.HomeworkId, item.CommunityId, item.Title, body, item.Revision, item.CreatedAt, item.UpdatedAt);
    }

    private static string MediaName(string kind, string? name)
    {
        var fallback = kind switch { "image" => "Фото", "video" => "Видео", "voice" => "Голосовое", "circle" => "Кружок", _ => "Документ" };
        var raw = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        var slash = Math.Max(raw.LastIndexOf('/'), raw.LastIndexOf('\\'));
        if (slash >= 0 && slash < raw.Length - 1) raw = raw[(slash + 1)..];
        if (raw.Length > 80) raw = raw[..80];
        try { return CommunityValidation.Message(raw); }
        catch (ArgumentException) { return fallback; }
    }

    private void Drop(Guid id)
    {
        if (archive is null) return;
        foreach (var key in new[] { ContentNames.GroupMessage(id), ContentNames.GroupFile(id) })
        {
            try { archive.Delete(key); }
            catch (Exception) { }
        }
    }

    private ChatMessageResponse StoreMessage(ChatMessageResponse message)
    {
        if (archive is null) return message;
        archive.Put(ContentNames.GroupMessage(message.MessageId), Encoding.UTF8.GetBytes(message.Body));
        return LoadMessage(message);
    }

    private ChatMessageResponse LoadMessage(ChatMessageResponse message)
    {
        if (message.Deleted) return message;
        var body = ReadText(ContentNames.GroupMessage(message.MessageId), message.Body);
        return body == message.Body ? message : new ChatMessageResponse(message.MessageId, message.ConversationId, message.SenderId, message.SenderName, body, message.CreatedAt, message.Kind, message.Deleted, message.ReplyTo);
    }

    private string ReadText(string key, string fallback)
    {
        if (archive is null) return fallback;
        var stored = archive.Get(key);
        return stored is null ? fallback : Encoding.UTF8.GetString(stored);
    }

    private Task<T> Run<T>(string bearer, Func<CommunityRepository, Task<T>> operation, CancellationToken ct)
        => trustedAccounts.ExecuteAsync(bearer, async (context, token) =>
        {
            try { return await operation(new(context, configuration, token)); }
            catch (ArgumentException) { throw CommunityServiceException.InvalidRequest(); }
        }, ct);
}
