using System.Text.Json.Serialization;

namespace Zapara.Contracts.Communities;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GroupTopicRequest
{
    [JsonConstructor]
    public GroupTopicRequest(string title, string icon)
    {
        Title = title ?? "";
        Icon = icon ?? "";
    }

    [JsonRequired, JsonInclude] public string Title { get; private init; }
    [JsonRequired, JsonInclude] public string Icon { get; private init; }
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
    public GroupTopicResponse(Guid? topicId, string title, string icon, string? lastBody, string? lastAuthor, DateTimeOffset? lastAt, int unread, bool canDelete)
    {
        TopicId = topicId;
        Title = title ?? "";
        Icon = icon ?? "";
        LastBody = lastBody;
        LastAuthor = lastAuthor;
        LastAt = lastAt;
        Unread = unread < 0 ? 0 : unread;
        CanDelete = canDelete;
    }

    [JsonRequired, JsonInclude] public Guid? TopicId { get; private init; }
    [JsonRequired, JsonInclude] public string Title { get; private init; }
    [JsonRequired, JsonInclude] public string Icon { get; private init; }
    [JsonRequired, JsonInclude] public string? LastBody { get; private init; }
    [JsonRequired, JsonInclude] public string? LastAuthor { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset? LastAt { get; private init; }
    [JsonRequired, JsonInclude] public int Unread { get; private init; }
    [JsonRequired, JsonInclude] public bool CanDelete { get; private init; }
}

public sealed record GroupTopicListResponse
{
    [JsonConstructor]
    public GroupTopicListResponse(IReadOnlyList<GroupTopicResponse> topics)
        => Topics = topics ?? Array.Empty<GroupTopicResponse>();

    [JsonRequired, JsonInclude] public IReadOnlyList<GroupTopicResponse> Topics { get; private init; }
}
