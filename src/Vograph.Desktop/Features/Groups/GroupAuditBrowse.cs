namespace Vograph.Desktop.Features.Groups;

public static class GroupAuditBrowse
{
    public static IReadOnlyList<string> Filter(IEnumerable<string> events, string query, int kind)
    {
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return events.Where(item =>
        {
            var text = Normalize(item);
            var matchesKind = kind switch
            {
                1 => text.Contains("тем", StringComparison.Ordinal),
                2 => text.Contains("рол", StringComparison.Ordinal),
                3 => text.Contains("доступ", StringComparison.Ordinal),
                _ => true
            };
            return matchesKind && words.All(word => text.Contains(word, StringComparison.Ordinal));
        }).ToArray();
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant().Replace('ё', 'е');
}
