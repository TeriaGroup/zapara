using System.Text.Json.Serialization;

namespace Zapara.Contracts.Communities;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GroupTopicRequest
{
    [JsonConstructor]
    public GroupTopicRequest(string title, string icon, string kind = "chat", string? description = null, string? accent = null, bool? pinned = null, string? writePolicy = null)
    {
        Title = title ?? "";
        Icon = icon ?? "";
        Kind = kind ?? "chat";
        Description = description;
        Accent = accent;
        Pinned = pinned;
        WritePolicy = writePolicy;
    }

    [JsonRequired, JsonInclude] public string Title { get; private init; }
    [JsonRequired, JsonInclude] public string Icon { get; private init; }
    [JsonInclude] public string Kind { get; private init; }
    [JsonInclude] public string? Description { get; private init; }
    [JsonInclude] public string? Accent { get; private init; }
    [JsonInclude] public bool? Pinned { get; private init; }
    [JsonInclude] public string? WritePolicy { get; private init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TopicMessageRequest
{
    [JsonConstructor]
    public TopicMessageRequest(string body, Guid? topicId, Guid? replyTo = null)
    {
        Body = CommunityValidation.Message(body);
        if (topicId == Guid.Empty) throw CommunityValidation.Invalid();
        TopicId = topicId;
        ReplyTo = replyTo is null || replyTo == Guid.Empty ? null : CommunityValidation.Id(replyTo.Value);
    }

    [JsonRequired, JsonInclude] public string Body { get; private init; }
    [JsonRequired, JsonInclude] public Guid? TopicId { get; private init; }
    [JsonInclude] public Guid? ReplyTo { get; private init; }
}

public sealed record GroupTopicResponse
{
    [JsonConstructor]
    public GroupTopicResponse(Guid? topicId, string title, string icon, string? lastBody, string? lastAuthor, DateTimeOffset? lastAt, int unread, bool canDelete, string kind = "chat", int activeBallots = 0, string description = "", string accent = "default", bool pinned = false, string writePolicy = "all", bool canPost = true)
    {
        TopicId = topicId;
        Title = title ?? "";
        Icon = icon ?? "";
        LastBody = lastBody;
        LastAuthor = lastAuthor;
        LastAt = lastAt;
        Unread = unread < 0 ? 0 : unread;
        CanDelete = canDelete;
        Kind = kind == "ballots" ? "ballots" : "chat";
        ActiveBallots = activeBallots < 0 ? 0 : activeBallots;
        Description = description ?? "";
        Accent = accent ?? "default";
        Pinned = pinned;
        WritePolicy = writePolicy ?? "all";
        CanPost = canPost;
    }

    [JsonRequired, JsonInclude] public Guid? TopicId { get; private init; }
    [JsonRequired, JsonInclude] public string Title { get; private init; }
    [JsonRequired, JsonInclude] public string Icon { get; private init; }
    [JsonRequired, JsonInclude] public string? LastBody { get; private init; }
    [JsonRequired, JsonInclude] public string? LastAuthor { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset? LastAt { get; private init; }
    [JsonRequired, JsonInclude] public int Unread { get; private init; }
    [JsonRequired, JsonInclude] public bool CanDelete { get; private init; }
    [JsonInclude] public string Kind { get; private init; }
    [JsonInclude] public int ActiveBallots { get; private init; }
    [JsonInclude] public string Description { get; private init; }
    [JsonInclude] public string Accent { get; private init; }
    [JsonInclude] public bool Pinned { get; private init; }
    [JsonInclude] public string WritePolicy { get; private init; }
    [JsonInclude] public bool CanPost { get; private init; }
}

public sealed record GroupTopicListResponse
{
    [JsonConstructor]
    public GroupTopicListResponse(IReadOnlyList<GroupTopicResponse> topics, bool canManageChannels = false)
    {
        Topics = topics ?? Array.Empty<GroupTopicResponse>();
        CanManageChannels = canManageChannels;
    }

    [JsonRequired, JsonInclude] public IReadOnlyList<GroupTopicResponse> Topics { get; private init; }
    [JsonInclude] public bool CanManageChannels { get; private init; }
}
