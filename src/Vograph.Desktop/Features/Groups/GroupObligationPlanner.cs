using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Groups;

public sealed record GroupObligationEntry(string Kind, Guid Id, Guid? TopicId, string Title,
    DateTimeOffset? Deadline, bool NeedsMe)
{
    public bool NeedsAction(DateTimeOffset now) => NeedsMe &&
        (Kind == "homework" || Deadline is null || Deadline > now);
    public string KindLabel => Kind switch { "homework" => "Домашка", "form" => "Анкета",
        "ballot" => "Голосование", _ => "Задание группы" };
    public string LabelAt(DateTimeOffset now) => $"{KindLabel} · {Title} · " +
        (Deadline is { } at ? at.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "без срока") +
        (NeedsAction(now) ? " · требуется действие" : "");
}

public static class GroupObligationPlanner
{
    public static IReadOnlyList<GroupObligationEntry> Merge(
        IEnumerable<GroupHomeworkCopyResponse> homework, IEnumerable<GroupFormResponse> forms,
        IEnumerable<BallotResponse> ballots, IEnumerable<GroupTopicResponse> topics)
    {
        var readable = topics.Where(topic => !topic.Archived && topic.Supported &&
            topic.Permissions.Contains("read")).ToArray();
        var rows = new List<GroupObligationEntry>();
        rows.AddRange(homework.Select(item => new GroupObligationEntry("homework", item.HomeworkId,
            item.TopicId, item.Title, item.DeadlineAt, item.CanComplete && !item.Completed)));
        rows.AddRange(forms.Where(item => readable.Any(topic => topic.TopicId == item.TopicId && topic.Kind == "forms"))
            .Select(item => new GroupObligationEntry("form", item.FormId, item.TopicId, item.Title,
                item.DeadlineAt, item.CanRespond && item.OwnResponse is null)));
        rows.AddRange(ballots.Where(item => item.TopicId is null || readable.Any(topic => topic.TopicId == item.TopicId && topic.Kind == "ballots"))
            .Select(item => new GroupObligationEntry("ballot", item.BallotId, item.TopicId,
                item.Question, item.DeadlineAt,
                item.Status == "open" && item.Options.All(option => !option.Chosen) &&
                (item.TopicId is null || readable.Any(topic => topic.TopicId == item.TopicId &&
                    topic.Permissions.Contains("vote"))))));
        return rows.OrderBy(row => row.Deadline ?? DateTimeOffset.MaxValue).ThenBy(row => row.Kind)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
