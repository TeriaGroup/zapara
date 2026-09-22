using System.Text.Json.Serialization;
using Zapara.Contracts.Accounts;

namespace Zapara.Contracts.Communities;

public sealed record ClassmateResponse
{
    [JsonConstructor]
    public ClassmateResponse(Guid userId, string username, string? displayName, string role, bool self)
    {
        UserId = CommunityValidation.Id(userId);
        Username = AccountValidation.Username(username);
        DisplayName = AccountValidation.DisplayName(displayName);
        Role = CommunityValidation.Role(role);
        Self = self;
    }
    [JsonRequired, JsonInclude] public Guid UserId { get; private init; }
    [JsonRequired, JsonInclude] public string Username { get; private init; }
    [JsonRequired, JsonInclude] public string? DisplayName { get; private init; }
    [JsonRequired, JsonInclude] public string Role { get; private init; }
    [JsonRequired, JsonInclude] public bool Self { get; private init; }
}

public sealed record ConversationResponse
{
    [JsonConstructor]
    public ConversationResponse(Guid conversationId, string kind, Guid communityId, string title, Guid? peerUserId,
        string? lastBody, DateTimeOffset? lastAt, int unread)
    {
        ConversationId = CommunityValidation.Id(conversationId);
        Kind = kind is "direct" or "group" ? kind : throw CommunityValidation.Invalid();
        CommunityId = CommunityValidation.Id(communityId);
        Title = title;
        PeerUserId = peerUserId is null ? null : CommunityValidation.Id(peerUserId.Value);
        if ((Kind == "group") != (PeerUserId is null)) throw CommunityValidation.Invalid();
        if ((lastBody is null) != (lastAt is null) || unread < 0) throw CommunityValidation.Invalid();
        LastBody = lastBody is null ? null : CommunityValidation.Message(lastBody);
        LastAt = lastAt is null ? null : CommunityValidation.Utc(lastAt.Value);
        Unread = unread;
    }
    [JsonRequired, JsonInclude] public Guid ConversationId { get; private init; }
    [JsonRequired, JsonInclude] public string Kind { get; private init; }
    [JsonRequired, JsonInclude] public Guid CommunityId { get; private init; }
    [JsonRequired, JsonInclude] public string Title { get; private init; }
    [JsonRequired, JsonInclude] public Guid? PeerUserId { get; private init; }
    [JsonRequired, JsonInclude] public string? LastBody { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset? LastAt { get; private init; }
    [JsonRequired, JsonInclude] public int Unread { get; private init; }
}

public sealed record GroupHomeResponse
{
    [JsonConstructor]
    public GroupHomeResponse(Guid communityId, string name, string? groupName, ConversationResponse groupChat,
        IReadOnlyList<ClassmateResponse> classmates, IReadOnlyList<ConversationResponse> directs)
    {
        CommunityId = CommunityValidation.Id(communityId);
        Name = name;
        GroupName = groupName;
        if (groupChat is null || groupChat.Kind != "group" || groupChat.CommunityId != CommunityId) throw CommunityValidation.Invalid();
        GroupChat = groupChat;
        Classmates = classmates ?? throw CommunityValidation.Invalid();
        Directs = directs ?? throw CommunityValidation.Invalid();
    }
    [JsonRequired, JsonInclude] public Guid CommunityId { get; private init; }
    [JsonRequired, JsonInclude] public string Name { get; private init; }
    [JsonRequired, JsonInclude] public string? GroupName { get; private init; }
    [JsonRequired, JsonInclude] public ConversationResponse GroupChat { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<ClassmateResponse> Classmates { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<ConversationResponse> Directs { get; private init; }
}

public sealed record ChatMessageResponse
{
    [JsonConstructor]
    public ChatMessageResponse(Guid messageId, Guid conversationId, Guid senderId, string senderName, string body, DateTimeOffset createdAt)
    {
        MessageId = CommunityValidation.Id(messageId);
        ConversationId = CommunityValidation.Id(conversationId);
        SenderId = CommunityValidation.Id(senderId);
        SenderName = senderName;
        Body = CommunityValidation.Message(body);
        CreatedAt = CommunityValidation.Utc(createdAt);
    }
    [JsonRequired, JsonInclude] public Guid MessageId { get; private init; }
    [JsonRequired, JsonInclude] public Guid ConversationId { get; private init; }
    [JsonRequired, JsonInclude] public Guid SenderId { get; private init; }
    [JsonRequired, JsonInclude] public string SenderName { get; private init; }
    [JsonRequired, JsonInclude] public string Body { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset CreatedAt { get; private init; }
}

public sealed record SendMessageRequest
{
    [JsonConstructor]
    public SendMessageRequest(string body) => Body = CommunityValidation.Message(body);
    [JsonRequired, JsonInclude] public string Body { get; private init; }
}

public sealed record ChatPageResponse
{
    [JsonConstructor]
    public ChatPageResponse(IReadOnlyList<ChatMessageResponse> messages, bool hasMore)
    {
        Messages = messages ?? throw CommunityValidation.Invalid();
        HasMore = hasMore;
    }
    [JsonRequired, JsonInclude] public IReadOnlyList<ChatMessageResponse> Messages { get; private init; }
    [JsonRequired, JsonInclude] public bool HasMore { get; private init; }
}

public sealed record OpenDirectRequest
{
    [JsonConstructor]
    public OpenDirectRequest(Guid communityId, Guid userId)
    {
        CommunityId = CommunityValidation.Id(communityId);
        UserId = CommunityValidation.Id(userId);
    }
    [JsonRequired, JsonInclude] public Guid CommunityId { get; private init; }
    [JsonRequired, JsonInclude] public Guid UserId { get; private init; }
}
