namespace Vograph.Desktop.Features.Groups;

public sealed record GroupLessonHint(DateTime Date, string Time, string Subject, string Room);
public sealed record GroupContextChannel(Guid? TopicId, string Kind, int Unread, int ActiveBallots);
public sealed record GroupChatContext(GroupLessonHint? NextLesson, int ActiveBallots, int Unread)
{
    public bool HasContent => NextLesson is not null || ActiveBallots > 0 || Unread > 0;

    public static GroupLessonHint? ForMatchingGroup(string? communityGroupName, string? selectedGroupName,
        Func<GroupLessonHint?> read)
    {
        if (string.IsNullOrWhiteSpace(communityGroupName) || string.IsNullOrWhiteSpace(selectedGroupName) ||
            !string.Equals(communityGroupName.Trim(), selectedGroupName.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;
        return read();
    }

    public static GroupChatContext Compose(IEnumerable<GroupContextChannel> channels, GroupLessonHint? nextLesson)
    {
        var real = channels.Where(row => row.Kind == "chat" || row.TopicId is not null).ToArray();
        return new(nextLesson,
            real.Where(row => row.Kind == "ballots").Sum(row => Math.Max(0, row.ActiveBallots)),
            real.Where(row => row.Kind == "chat").Sum(row => Math.Max(0, row.Unread)));
    }
}
