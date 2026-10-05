namespace Vograph.Desktop.Features.Communities;

public static class CommunityBrowse
{
    public static IReadOnlyList<T> Filter<T>(IEnumerable<T> rows, string? query,
        Func<T, string?> name, Func<T, string?> description)
    {
        var needle = query?.Trim() ?? "";
        return needle.Length == 0 ? rows.ToArray() : rows.Where(row =>
            (name(row)?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (description(row)?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)).ToArray();
    }
}
