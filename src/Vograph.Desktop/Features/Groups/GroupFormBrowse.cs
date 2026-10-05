namespace Vograph.Desktop.Features.Groups;

public static class GroupFormBrowse
{
    public static IReadOnlyList<SpaceFormRow> Filter(IEnumerable<SpaceFormRow> rows, string query, int status)
    {
        var words = query.Trim().ToLowerInvariant().Replace('ё', 'е')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return rows.Where(row =>
        {
            var form = row.Form;
            var matchesStatus = status switch
            {
                1 => form.CanRespond && form.OwnResponse is null,
                2 => form.OwnResponse is not null,
                3 => !form.CanRespond,
                _ => true
            };
            var text = (form.Title + " " + form.Description).ToLowerInvariant().Replace('ё', 'е');
            return matchesStatus && words.All(word => text.Contains(word, StringComparison.Ordinal));
        }).ToArray();
    }
}
