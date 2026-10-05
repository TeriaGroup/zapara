namespace Vograph.Desktop.Features.Groups;

public static class GroupMessageBrowse
{
    public static IReadOnlyList<GroupMessageRow> Filter(IEnumerable<GroupMessageRow> rows, string query,
        int authorIndex, int kindIndex)
    {
        var needle = query.Trim();
        authorIndex = authorIndex is >= 1 and <= 2 ? authorIndex : 0;
        kindIndex = kindIndex is >= 1 and <= 4 ? kindIndex : 0;
        return rows.Where(row => (!row.Deleted || needle.Length == 0 && authorIndex == 0 && kindIndex == 0)
                && (authorIndex == 0 || row.Mine == (authorIndex == 1))
                && (kindIndex == 0 || KindMatches(row.Kind, kindIndex))
                && (needle.Length == 0 || row.Body.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    public static string? CopyText(GroupMessageRow row)
        => row.Deleted || row.Kind != "text" || string.IsNullOrWhiteSpace(row.Body) ? null : row.Body;

    private static bool KindMatches(string kind, int index) => index switch
    {
        1 => kind == "text",
        2 => kind is "image" or "video",
        3 => kind == "file",
        4 => kind is "voice" or "circle",
        _ => false
    };
}
