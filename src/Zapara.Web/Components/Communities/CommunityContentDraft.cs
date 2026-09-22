namespace Zapara.Web.Components.Communities;

public sealed record CommunityContentDraft(string Title, string Body);
public sealed record CommunityPollDraft(string Question, DateTimeOffset Deadline, IReadOnlyList<string> Options);
