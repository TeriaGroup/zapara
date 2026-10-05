namespace Vograph.Desktop.Features.Groups;

public static class GroupMaterialBrowse
{
    public static IReadOnlyList<GroupMessageRow> Filter(IEnumerable<GroupMessageRow> rows, string query)
    {
        var words = query.Trim().ToLowerInvariant().Replace('ё', 'е')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return rows.Where(row =>
        {
            var text = (row.Body + " " + row.Author + " " + row.Kind).ToLowerInvariant().Replace('ё', 'е');
            return words.All(word => text.Contains(word, StringComparison.Ordinal));
        }).ToArray();
    }
}
