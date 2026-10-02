using Vograph.Core.Services;

namespace Vograph.Desktop.Features.Homeworks;

internal static class HomeworkBrowse
{
    public static bool MatchesStatus(bool done, int filter) => filter == 2 || done == (filter == 1);

    public static bool MatchesDeadline(DateTime? due, DateTime today, int filter)
    {
        if (filter == 0) return true;
        if (filter == 4) return due is null;
        if (due is null) return false;
        var days = (due.Value.Date - today.Date).Days;
        return filter switch
        {
            1 => days < 0,
            2 => days is >= 0 and <= 1,
            3 => days is >= 2 and <= 7,
            _ => true
        };
    }

    public static bool MatchesSharedDeadline(DateTimeOffset? due, DateTime now, int filter)
    {
        if (filter == 0) return true;
        if (filter == 4) return due is null;
        if (due is null) return false;
        var local = due.Value.LocalDateTime;
        if (filter == 1) return local < now;
        if (local < now) return false;
        var days = (local.Date - now.Date).Days;
        return filter switch { 2 => days is >= 0 and <= 1, 3 => days is >= 2 and <= 7, _ => true };
    }

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
