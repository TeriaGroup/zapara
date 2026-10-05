using Vograph.Core.Models;

namespace Vograph.Desktop.Features.Friends;

public static class FriendBrowse
{
    public static IReadOnlyList<FriendGroup> Filter(IEnumerable<FriendGroup> groups, string query, int enabledFilter) =>
        groups.Where(group => Matches(group.GroupName, group.MemberNames ?? "", group.Enabled, query, enabledFilter)).ToArray();

    public static bool Matches(string groupName, string memberNames, bool enabled, string query, int enabledFilter)
    {
        if (enabledFilter == 1 && !enabled || enabledFilter == 2 && enabled) return false;
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var text = Normalize(groupName + " " + memberNames);
        return words.All(word => text.Contains(word, StringComparison.Ordinal));
    }

    public static IReadOnlyList<FriendEncounterViewModel> Encounters(
        IEnumerable<FriendEncounterViewModel> encounters, DateTime today, int dayFilter, bool showAll,
        string? groupName = null)
    {
        var filtered = encounters.Where(hit => (string.IsNullOrWhiteSpace(groupName) ||
            hit.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase)) && (dayFilter switch
        {
            1 => hit.Date.Date == today.Date,
            2 => hit.Date.Date == today.Date.AddDays(1),
            _ => true
        }));
        return (showAll ? filtered : filtered.Take(3)).ToArray();
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant().Replace('ё', 'е');
}
