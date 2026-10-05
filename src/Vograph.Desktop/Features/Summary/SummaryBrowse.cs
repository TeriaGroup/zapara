namespace Vograph.Desktop.Features.Summary;

public static class SummaryBrowse
{
    public static IReadOnlyList<CountItem> Sort(IEnumerable<CountItem> rows, int mode)
    {
        var byName = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), true);
        return mode == 1
            ? rows.OrderBy(row => row.Name, byName).ThenByDescending(row => row.Count).ToArray()
            : rows.OrderByDescending(row => row.Count).ThenBy(row => row.Name, byName).ToArray();
    }
}
