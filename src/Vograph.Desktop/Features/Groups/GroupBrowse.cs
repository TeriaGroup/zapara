namespace Vograph.Desktop.Features.Groups;

public static class GroupBrowse
{
    public static IReadOnlyList<GroupCommunityRow> Communities(IEnumerable<GroupCommunityRow> rows, string query)
        => rows.Where(row => Matches(query, row.Name)).ToArray();

    public static IReadOnlyList<GroupPersonRow> People(IEnumerable<GroupPersonRow> rows, string query)
        => rows.Where(row => Matches(query, row.Name, row.Detail)).ToArray();

    public static IReadOnlyList<GroupChannelRow> Channels(IEnumerable<GroupChannelRow> rows, string query,
        string kind, bool unreadOnly)
        => rows.Where(row => (kind == "all" || row.Kind == kind)
                && (!unreadOnly || row.Unread.Length > 0)
                && Matches(query, row.Title, row.Description))
            .OrderBy(row => row.TopicId is null && row.Kind == "chat" ? 0 : row.IsGlobalBallots ? 3 : row.Pinned ? 1 : 2)
            .ThenByDescending(row => row.Unread.Length > 0)
            .ToArray();

    public static GroupChannelRow? NextUnread(IEnumerable<GroupChannelRow> rows, GroupChannelRow? selected)
    {
        var real = rows.Where(row => !row.IsGlobalBallots).ToArray();
        var current = Array.IndexOf(real, selected);
        for (var offset = 1; offset <= real.Length; offset++)
        {
            var candidate = real[(current + offset) % real.Length];
            if (candidate != selected && candidate.UnreadCount > 0) return candidate;
        }
        return null;
    }

    private static bool Matches(string query, params string[] values)
    {
        var needle = query.Trim();
        return needle.Length == 0 || values.Any(value => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
