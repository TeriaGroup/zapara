using System.Text.Json.Serialization;

namespace Zapara.Contracts.Communities;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HomeworkUpsert
{
    [JsonConstructor]
    public HomeworkUpsert(string title, string body, long expectedRevision, DateTimeOffset? deadlineAt = null, Guid? topicId = null)
    {
        (Title, Body, ExpectedRevision) = (CommunityValidation.Title(title), CommunityValidation.Body(body), CommunityValidation.Revision(expectedRevision));
        DeadlineAt = deadlineAt is null ? null : CommunityValidation.Utc(deadlineAt.Value);
        TopicId = topicId is null ? null : CommunityValidation.Id(topicId.Value);
    }
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Guid? TopicId { get; private init; }
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? DeadlineAt { get; private init; }
    [JsonRequired, JsonInclude] public string Title { get; private init; }
    [JsonRequired, JsonInclude] public string Body { get; private init; }
    [JsonRequired, JsonInclude] public long ExpectedRevision { get; private init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompletionUpsert
{
    [JsonConstructor]
    public CompletionUpsert(bool completed, long expectedRevision)
        => (Completed, ExpectedRevision) = (completed, CommunityValidation.Revision(expectedRevision));
    [JsonRequired, JsonInclude] public bool Completed { get; private init; }
    [JsonRequired, JsonInclude] public long ExpectedRevision { get; private init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementUpsert
{
    [JsonConstructor]
    public AnnouncementUpsert(string title, string body, long expectedRevision)
        => (Title, Body, ExpectedRevision) = (CommunityValidation.Title(title), CommunityValidation.Body(body), CommunityValidation.Revision(expectedRevision));
    [JsonRequired, JsonInclude] public string Title { get; private init; }
    [JsonRequired, JsonInclude] public string Body { get; private init; }
    [JsonRequired, JsonInclude] public long ExpectedRevision { get; private init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PollUpsert
{
    [JsonConstructor]
    public PollUpsert(string question, DateTimeOffset deadlineAt, IReadOnlyList<string> options, long expectedRevision)
    {
        Question = CommunityValidation.Question(question);
        DeadlineAt = CommunityValidation.Utc(deadlineAt);
        Options = CommunityValidation.Options(options);
        ExpectedRevision = CommunityValidation.Revision(expectedRevision);
    }
    [JsonRequired, JsonInclude] public string Question { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset DeadlineAt { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<string> Options { get; private init; }
    [JsonRequired, JsonInclude] public long ExpectedRevision { get; private init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VoteRequest
{
    [JsonConstructor]
    public VoteRequest(Guid optionId) => OptionId = CommunityValidation.Id(optionId);
    [JsonRequired, JsonInclude] public Guid OptionId { get; private init; }
}
