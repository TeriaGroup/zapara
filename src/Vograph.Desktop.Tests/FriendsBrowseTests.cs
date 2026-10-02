using Vograph.Core.Models;
using Vograph.Desktop.Features.Friends;
using Xunit;

namespace Vograph.Desktop.Tests;

public class FriendsBrowseTests
{
    [Fact]
    public void Search_and_enabled_filter_combine_without_changing_source()
    {
        FriendGroup[] groups =
        [
            new() { GroupName = "09С31", MemberNames = "Иван", Enabled = true },
            new() { GroupName = "Е452Б", MemberNames = "Иван и Пётр", Enabled = false },
            new() { GroupName = "О311", MemberNames = "Ольга", Enabled = true }
        ];

        Assert.Equal(["09С31"], FriendBrowse.Filter(groups, "иван", 1).Select(group => group.GroupName));
        Assert.Equal(["Е452Б"], FriendBrowse.Filter(groups, "иван", 2).Select(group => group.GroupName));
        Assert.Equal(3, groups.Length);
    }

    [Fact]
    public void Encounter_day_filter_and_collapse_keep_order()
    {
        var today = new DateTime(2026, 9, 14);
        var all = Enumerable.Range(0, 5).Select(offset => new FriendEncounterViewModel(
            "", "Группа", "", "", 0, today.AddDays(offset), "Матан", "09:00", "scope", "group", 1)).ToArray();

        Assert.Equal([today], FriendBrowse.Encounters(all, today, 1, true).Select(hit => hit.Date));
        Assert.Equal([today.AddDays(1)], FriendBrowse.Encounters(all, today, 2, true).Select(hit => hit.Date));
        Assert.Equal(3, FriendBrowse.Encounters(all, today, 0, false).Count);
        Assert.Equal(5, FriendBrowse.Encounters(all, today, 0, true).Count);
    }

    [Fact]
    public void Encounter_group_filter_intersects_selected_day()
    {
        var today = new DateTime(2026, 9, 14);
        FriendEncounterViewModel[] all =
        [
            new("", "А", "", "", 0, today, "Матан", "09:00", "scope", "group", 1),
            new("", "Б", "", "", 0, today, "Физика", "10:40", "scope", "group", 1),
            new("", "А", "", "", 0, today.AddDays(1), "Матан", "09:00", "scope", "group", 1)
        ];

        Assert.Equal(["А"], FriendBrowse.Encounters(all, today, 1, true, "А").Select(hit => hit.GroupName));
    }
}
