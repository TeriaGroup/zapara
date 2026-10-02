namespace Vograph.Desktop.Features.Week;

public static class WeekBrowse
{
    public static IReadOnlyList<WeekDay> Filter(IEnumerable<WeekDay> days, string query, bool onlyClasses)
    {
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return days.Select(day => words.Length == 0 ? day : day with
            {
                Rows = day.Rows.Where(row =>
                {
                    var text = Normalize(string.Join(' ', row.Name, row.SubjectRaw, row.Teacher, row.Room, row.TypeLabel));
                    return words.All(word => text.Contains(word, StringComparison.Ordinal));
                }).ToArray()
            })
            .Where(day => !onlyClasses && words.Length == 0 || day.Rows.Count > 0)
            .ToArray();
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant().Replace('ё', 'е');
}
