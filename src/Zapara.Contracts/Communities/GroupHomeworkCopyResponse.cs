using System.Text.Json.Serialization;

namespace Zapara.Contracts.Communities;

public sealed record GroupHomeworkCopyResponse
{
    [JsonConstructor]
    public GroupHomeworkCopyResponse(Guid homeworkId, string title, string body, long revision, bool completed, long completionRevision, DateTimeOffset? deadlineAt = null, Guid? topicId = null)
    {
        DeadlineAt = deadlineAt; TopicId = topicId;
        HomeworkId = homeworkId;
        Title = title ?? "";
        Body = body ?? "";
        Revision = revision < 1 ? 1 : revision;
        Completed = completed;
        CompletionRevision = completionRevision < 0 ? 0 : completionRevision;
    }

    [JsonInclude] public Guid? TopicId { get; private init; }
    [JsonInclude] public DateTimeOffset? DeadlineAt { get; private init; }
    [JsonRequired, JsonInclude] public Guid HomeworkId { get; private init; }
    [JsonRequired, JsonInclude] public string Title { get; private init; }
    [JsonRequired, JsonInclude] public string Body { get; private init; }
    [JsonRequired, JsonInclude] public long Revision { get; private init; }
    [JsonRequired, JsonInclude] public bool Completed { get; private init; }
    [JsonRequired, JsonInclude] public long CompletionRevision { get; private init; }
}
