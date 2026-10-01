using Vograph.Core.Services;

namespace Vograph.Desktop.Features.Homeworks;

internal static class HomeworkBrowse
{
    public static bool MatchesStatus(bool done, int filter) => filter == 2 || done == (filter == 1);

    public static bool MatchesSubject(string subject, string filter) =>
        filter.Length == 0 || ParityService.NormalizeSubject(subject) == ParityService.NormalizeSubject(filter);

    public static bool MatchesQuery(string query, params string[] fields)
    {
        var words = query.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return true;
        var text = string.Join(' ', fields).ToLowerInvariant();
        return words.All(word => text.Contains(word.ToLowerInvariant(), StringComparison.Ordinal));
    }
}
