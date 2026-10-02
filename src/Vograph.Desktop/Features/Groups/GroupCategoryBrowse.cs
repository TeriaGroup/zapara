namespace Vograph.Desktop.Features.Groups;

public static class GroupCategoryBrowse
{
    public static IReadOnlyList<SpaceCategoryBucket> Filter(IEnumerable<SpaceCategoryBucket> categories, string query)
    {
        var words = query.Trim().ToLowerInvariant().Replace('ё', 'е')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return categories.Where(category =>
        {
            var name = category.Title.ToLowerInvariant().Replace('ё', 'е');
            return words.All(word => name.Contains(word, StringComparison.Ordinal));
        }).ToArray();
    }
}
