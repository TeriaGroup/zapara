using System.Text.Json.Serialization;

namespace Zapara.Contracts.Communities;

public sealed record CommunityResponse
{
    [JsonConstructor]
    public CommunityResponse(Guid communityId, string name, string description, long revision, string? role)
    {
        CommunityId = CommunityValidation.Id(communityId);
        Name = CommunityValidation.Name(name);
        Description = CommunityValidation.Description(description);
        Revision = revision > 0 ? revision : throw CommunityValidation.Invalid();
        Role = role is null ? null : CommunityValidation.Role(role);
    }
    [JsonRequired, JsonInclude] public Guid CommunityId { get; private init; }
    [JsonRequired, JsonInclude] public string Name { get; private init; }
    [JsonRequired, JsonInclude] public string Description { get; private init; }
    [JsonRequired, JsonInclude] public long Revision { get; private init; }
    [JsonRequired, JsonInclude] public string? Role { get; private init; }
}

public sealed record JoinRequestResponse
{
    [JsonConstructor]
    public JoinRequestResponse(Guid requestId, Guid communityId, Guid userId, string status, DateTimeOffset createdAt)
    {
        RequestId = CommunityValidation.Id(requestId);
        CommunityId = CommunityValidation.Id(communityId);
        UserId = CommunityValidation.Id(userId);
        Status = CommunityValidation.JoinStatus(status);
        CreatedAt = CommunityValidation.Utc(createdAt);
    }
    [JsonRequired, JsonInclude] public Guid RequestId { get; private init; }
    [JsonRequired, JsonInclude] public Guid CommunityId { get; private init; }
    [JsonRequired, JsonInclude] public Guid UserId { get; private init; }
    [JsonRequired, JsonInclude] public string Status { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset CreatedAt { get; private init; }
}

public sealed record MemberResponse
{
    [JsonConstructor]
    public MemberResponse(Guid userId, string role)
        => (UserId, Role) = (CommunityValidation.Id(userId), CommunityValidation.Role(role));
    [JsonRequired, JsonInclude] public Guid UserId { get; private init; }
    [JsonRequired, JsonInclude] public string Role { get; private init; }
}

public sealed record HomeworkResponse
{
    [JsonConstructor]
    public HomeworkResponse(Guid homeworkId, Guid communityId, string title, string body, long revision,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        HomeworkId = CommunityValidation.Id(homeworkId);
        CommunityId = CommunityValidation.Id(communityId);
        Title = CommunityValidation.Title(title);
        Body = CommunityValidation.Body(body);
        Revision = revision > 0 ? revision : throw CommunityValidation.Invalid();
        CreatedAt = CommunityValidation.Utc(createdAt);
        UpdatedAt = CommunityValidation.Utc(updatedAt);
    }
    [JsonRequired, JsonInclude] public Guid HomeworkId { get; private init; }
    [JsonRequired, JsonInclude] public Guid CommunityId { get; private init; }
    [JsonRequired, JsonInclude] public string Title { get; private init; }
    [JsonRequired, JsonInclude] public string Body { get; private init; }
    [JsonRequired, JsonInclude] public long Revision { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset CreatedAt { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset UpdatedAt { get; private init; }
}

public sealed record CompletionResponse
{
    [JsonConstructor]
    public CompletionResponse(Guid homeworkId, bool completed, long revision, DateTimeOffset? updatedAt)
    {
        HomeworkId = CommunityValidation.Id(homeworkId);
        Completed = completed;
        Revision = CommunityValidation.Revision(revision);
        UpdatedAt = updatedAt is null ? null : CommunityValidation.Utc(updatedAt.Value);
        if (revision == 0 && completed) throw CommunityValidation.Invalid();
    }
    [JsonRequired, JsonInclude] public Guid HomeworkId { get; private init; }
    [JsonRequired, JsonInclude] public bool Completed { get; private init; }
    [JsonRequired, JsonInclude] public long Revision { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset? UpdatedAt { get; private init; }
}

public sealed record AnnouncementResponse
{
    [JsonConstructor]
    public AnnouncementResponse(Guid announcementId, Guid communityId, string title, string body, long revision,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        AnnouncementId = CommunityValidation.Id(announcementId);
        CommunityId = CommunityValidation.Id(communityId);
        Title = CommunityValidation.Title(title);
        Body = CommunityValidation.Body(body);
        Revision = revision > 0 ? revision : throw CommunityValidation.Invalid();
        CreatedAt = CommunityValidation.Utc(createdAt);
        UpdatedAt = CommunityValidation.Utc(updatedAt);
    }
    [JsonRequired, JsonInclude] public Guid AnnouncementId { get; private init; }
    [JsonRequired, JsonInclude] public Guid CommunityId { get; private init; }
    [JsonRequired, JsonInclude] public string Title { get; private init; }
    [JsonRequired, JsonInclude] public string Body { get; private init; }
    [JsonRequired, JsonInclude] public long Revision { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset CreatedAt { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset UpdatedAt { get; private init; }
}

public sealed record PollOptionResponse
{
    [JsonConstructor]
    public PollOptionResponse(Guid optionId, string label, int ordinal)
    {
        OptionId = CommunityValidation.Id(optionId);
        Label = CommunityValidation.Option(label);
        Ordinal = ordinal > 0 ? ordinal : throw CommunityValidation.Invalid();
    }
    [JsonRequired, JsonInclude] public Guid OptionId { get; private init; }
    [JsonRequired, JsonInclude] public string Label { get; private init; }
    [JsonRequired, JsonInclude] public int Ordinal { get; private init; }
}

public sealed record PollResponse
{
    [JsonConstructor]
    public PollResponse(Guid pollId, Guid communityId, string question, DateTimeOffset deadlineAt, long revision,
        IReadOnlyList<PollOptionResponse> options)
    {
        PollId = CommunityValidation.Id(pollId);
        CommunityId = CommunityValidation.Id(communityId);
        Question = CommunityValidation.Question(question);
        DeadlineAt = CommunityValidation.Utc(deadlineAt);
        Revision = revision > 0 ? revision : throw CommunityValidation.Invalid();
        if (options is null || options.Count is < 2 or > 16) throw CommunityValidation.Invalid();
        Options = Array.AsReadOnly(options.ToArray());
    }
    [JsonRequired, JsonInclude] public Guid PollId { get; private init; }
    [JsonRequired, JsonInclude] public Guid CommunityId { get; private init; }
    [JsonRequired, JsonInclude] public string Question { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset DeadlineAt { get; private init; }
    [JsonRequired, JsonInclude] public long Revision { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<PollOptionResponse> Options { get; private init; }
}

public sealed record VoteResponse
{
    [JsonConstructor]
    public VoteResponse(Guid pollId, Guid optionId, DateTimeOffset createdAt)
    {
        PollId = CommunityValidation.Id(pollId);
        OptionId = CommunityValidation.Id(optionId);
        CreatedAt = CommunityValidation.Utc(createdAt);
    }
    [JsonRequired, JsonInclude] public Guid PollId { get; private init; }
    [JsonRequired, JsonInclude] public Guid OptionId { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset CreatedAt { get; private init; }
}

public sealed record PollOptionResult
{
    [JsonConstructor]
    public PollOptionResult(Guid optionId, string label, int votes)
    {
        OptionId = CommunityValidation.Id(optionId);
        Label = CommunityValidation.Option(label);
        Votes = votes >= 0 ? votes : throw CommunityValidation.Invalid();
    }
    [JsonRequired, JsonInclude] public Guid OptionId { get; private init; }
    [JsonRequired, JsonInclude] public string Label { get; private init; }
    [JsonRequired, JsonInclude] public int Votes { get; private init; }
}

public sealed record PollResultsResponse
{
    [JsonConstructor]
    public PollResultsResponse(Guid pollId, int totalVotes, IReadOnlyList<PollOptionResult> options)
    {
        PollId = CommunityValidation.Id(pollId);
        TotalVotes = totalVotes >= 0 ? totalVotes : throw CommunityValidation.Invalid();
        if (options is null || options.Count is < 2 or > 16) throw CommunityValidation.Invalid();
        if (options.Sum(o => o.Votes) != totalVotes) throw CommunityValidation.Invalid();
        Options = Array.AsReadOnly(options.ToArray());
    }
    [JsonRequired, JsonInclude] public Guid PollId { get; private init; }
    [JsonRequired, JsonInclude] public int TotalVotes { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<PollOptionResult> Options { get; private init; }
}

public sealed record CommunityError
{
    [JsonConstructor]
    public CommunityError(string title, int status, string code)
    {
        if (string.IsNullOrWhiteSpace(title)) throw CommunityValidation.Invalid();
        Title = title;
        Status = status is 400 or 401 or 403 or 404 or 409 or 413 or 415 or 429 or 500 or 503 ? status : throw CommunityValidation.Invalid();
        Code = code is "invalid_request" or "invalid_session" or "forbidden" or "not_found" or "revision_conflict"
            or "already_voted" or "already_member" or "already_requested" or "poll_closed" or "payload_too_large"
            or "rate_limited" or "db_unavailable" or "internal_error" ? code : throw CommunityValidation.Invalid();
    }
    [JsonRequired, JsonInclude] public string Title { get; private init; }
    [JsonRequired, JsonInclude] public int Status { get; private init; }
    [JsonRequired, JsonInclude] public string Code { get; private init; }
}
