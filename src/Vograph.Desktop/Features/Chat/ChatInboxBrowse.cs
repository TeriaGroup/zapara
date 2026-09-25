namespace Vograph.Desktop.Features.Chat;

public static class ChatInboxBrowse
{
    public static IReadOnlyList<ChatInboxRow> Filter(IEnumerable<ChatInboxRow> rows, string query, int sourceIndex)
    {
        var needle = query.Trim();
        sourceIndex = sourceIndex is >= 1 and <= 3 ? sourceIndex : 0;
        return rows.Where(row => (sourceIndex == 0 || row.SourceIndex == sourceIndex)
                && (needle.Length == 0 || row.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || row.Preview.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    public static int UnreadTotal(IEnumerable<ChatInboxRow> rows) => rows.Sum(row => row.UnreadCount);
}
