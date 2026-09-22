using System.Text.Json.Serialization;

namespace Zapara.Contracts.Social;

public sealed record SocialHomeResponse
{
    [JsonConstructor]
    public SocialHomeResponse(string code, IReadOnlyList<SocialFriendResponse> friends,
        IReadOnlyList<SocialInviteResponse> incoming, IReadOnlyList<SocialInviteResponse> outgoing)
    {
        Code = code;
        Friends = friends ?? throw new ArgumentException();
        Incoming = incoming ?? throw new ArgumentException();
        Outgoing = outgoing ?? throw new ArgumentException();
    }
    [JsonRequired, JsonInclude] public string Code { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<SocialFriendResponse> Friends { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<SocialInviteResponse> Incoming { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<SocialInviteResponse> Outgoing { get; private init; }
}

public sealed record SocialFriendResponse
{
    [JsonConstructor]
    public SocialFriendResponse(Guid userId, string username, string? displayName, Guid conversationId,
        string? lastBody, DateTimeOffset? lastAt, int unread)
    {
        UserId = userId;
        Username = username;
        DisplayName = displayName;
        ConversationId = conversationId;
        LastBody = lastBody;
        LastAt = lastAt;
        Unread = unread;
    }
    [JsonRequired, JsonInclude] public Guid UserId { get; private init; }
    [JsonRequired, JsonInclude] public string Username { get; private init; }
    [JsonRequired, JsonInclude] public string? DisplayName { get; private init; }
    [JsonRequired, JsonInclude] public Guid ConversationId { get; private init; }
    [JsonRequired, JsonInclude] public string? LastBody { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset? LastAt { get; private init; }
    [JsonRequired, JsonInclude] public int Unread { get; private init; }
}

public sealed record SocialInviteResponse
{
    [JsonConstructor]
    public SocialInviteResponse(Guid friendshipId, string username, string? displayName, DateTimeOffset createdAt)
    {
        FriendshipId = friendshipId;
        Username = username;
        DisplayName = displayName;
        CreatedAt = createdAt;
    }
    [JsonRequired, JsonInclude] public Guid FriendshipId { get; private init; }
    [JsonRequired, JsonInclude] public string Username { get; private init; }
    [JsonRequired, JsonInclude] public string? DisplayName { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset CreatedAt { get; private init; }
}

public sealed record SocialInviteRequest
{
    [JsonConstructor]
    public SocialInviteRequest(string code) => Code = code;
    [JsonRequired, JsonInclude] public string Code { get; private init; }
}

public sealed record SocialTextRequest
{
    [JsonConstructor]
    public SocialTextRequest(string body, Guid? replyTo = null)
    {
        Body = body;
        ReplyTo = replyTo is Guid id && id != Guid.Empty ? id : null;
    }
    [JsonRequired, JsonInclude] public string Body { get; private init; }
    [JsonInclude] public Guid? ReplyTo { get; private init; }
}

public sealed record SocialCardRequest
{
    [JsonConstructor]
    public SocialCardRequest(string body, Guid? replyTo = null)
    {
        Body = body;
        ReplyTo = replyTo is Guid id && id != Guid.Empty ? id : null;
    }
    [JsonRequired, JsonInclude] public string Body { get; private init; }
    [JsonInclude] public Guid? ReplyTo { get; private init; }
}

public sealed record SocialStickerRequest
{
    [JsonConstructor]
    public SocialStickerRequest(string sticker, Guid? replyTo = null)
    {
        Sticker = sticker;
        ReplyTo = replyTo is Guid id && id != Guid.Empty ? id : null;
    }
    [JsonRequired, JsonInclude] public string Sticker { get; private init; }
    [JsonInclude] public Guid? ReplyTo { get; private init; }
}

public sealed record SocialReactionRequest
{
    [JsonConstructor]
    public SocialReactionRequest(string emoji) => Emoji = emoji ?? "";
    [JsonRequired, JsonInclude] public string Emoji { get; private init; }
}

public sealed record SocialReactionResponse
{
    [JsonConstructor]
    public SocialReactionResponse(string emoji, int count, bool mine)
    {
        Emoji = emoji;
        Count = count;
        Mine = mine;
    }
    [JsonRequired, JsonInclude] public string Emoji { get; private init; }
    [JsonRequired, JsonInclude] public int Count { get; private init; }
    [JsonRequired, JsonInclude] public bool Mine { get; private init; }
}

public sealed record SocialMessageResponse
{
    [JsonConstructor]
    public SocialMessageResponse(Guid messageId, Guid senderId, string senderName, string kind, string? body,
        Guid? attachmentId, string? fileName, string? contentType, long? bytes, DateTimeOffset createdAt,
        Guid? replyTo, string? replyBody, DateTimeOffset? editedAt, bool deleted, bool read, int? durationMs,
        IReadOnlyList<SocialReactionResponse> reactions)
    {
        MessageId = messageId;
        SenderId = senderId;
        SenderName = senderName;
        Kind = kind;
        Body = body;
        AttachmentId = attachmentId;
        FileName = fileName;
        ContentType = contentType;
        Bytes = bytes;
        CreatedAt = createdAt;
        ReplyTo = replyTo;
        ReplyBody = replyBody;
        EditedAt = editedAt;
        Deleted = deleted;
        Read = read;
        DurationMs = durationMs;
        Reactions = reactions ?? throw new ArgumentException();
    }
    [JsonRequired, JsonInclude] public Guid MessageId { get; private init; }
    [JsonRequired, JsonInclude] public Guid SenderId { get; private init; }
    [JsonRequired, JsonInclude] public string SenderName { get; private init; }
    [JsonRequired, JsonInclude] public string Kind { get; private init; }
    [JsonRequired, JsonInclude] public string? Body { get; private init; }
    [JsonRequired, JsonInclude] public Guid? AttachmentId { get; private init; }
    [JsonRequired, JsonInclude] public string? FileName { get; private init; }
    [JsonRequired, JsonInclude] public string? ContentType { get; private init; }
    [JsonRequired, JsonInclude] public long? Bytes { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset CreatedAt { get; private init; }
    [JsonRequired, JsonInclude] public Guid? ReplyTo { get; private init; }
    [JsonRequired, JsonInclude] public string? ReplyBody { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset? EditedAt { get; private init; }
    [JsonRequired, JsonInclude] public bool Deleted { get; private init; }
    [JsonRequired, JsonInclude] public bool Read { get; private init; }
    [JsonRequired, JsonInclude] public int? DurationMs { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<SocialReactionResponse> Reactions { get; private init; }
}

public sealed record SocialPageResponse
{
    [JsonConstructor]
    public SocialPageResponse(IReadOnlyList<SocialMessageResponse> messages, bool hasMore)
    {
        Messages = messages ?? throw new ArgumentException();
        HasMore = hasMore;
    }
    [JsonRequired, JsonInclude] public IReadOnlyList<SocialMessageResponse> Messages { get; private init; }
    [JsonRequired, JsonInclude] public bool HasMore { get; private init; }
}
