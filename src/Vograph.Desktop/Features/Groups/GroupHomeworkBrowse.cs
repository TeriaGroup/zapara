namespace Vograph.Desktop.Features.Groups;

public static class GroupHomeworkBrowse
{
    public static IReadOnlyList<SpaceHomeworkRow> Sort(IEnumerable<SpaceHomeworkRow> rows, int mode)
    {
        var byName = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), true);
        return mode switch
        {
            1 => rows.OrderBy(row => row.Item.DeadlineAt ?? DateTimeOffset.MaxValue)
                .ThenBy(row => row.Title, byName).ToArray(),
            2 => rows.OrderBy(row => row.Title, byName)
                .ThenBy(row => row.Item.DeadlineAt ?? DateTimeOffset.MaxValue).ToArray(),
            _ => rows.ToArray()
        };
    }
}
