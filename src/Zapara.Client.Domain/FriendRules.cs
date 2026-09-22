using Vograph.Core.Models;
using System.Text.RegularExpressions;
using Zapara.Contracts.Sync;

namespace Zapara.Client.Domain;

public sealed record FriendSchedule(FriendValue Friend, ScheduleSnapshot? Snapshot);
public sealed record FriendPresence(FriendValue Friend, int Score, bool Present, string Label, bool HasCompatibleSchedule);

public static class FriendRules
{
    public static int Score(Lesson mine, Lesson theirs)
    {
        if (!TimeSpan.TryParse(mine.TimeStart, out var a) || !TimeSpan.TryParse(theirs.TimeStart, out var b)) return 0;
        var endA = TimeSpan.TryParse(mine.TimeEnd, out var ea) ? ea : a.Add(TimeSpan.FromMinutes(95));
        var endB = TimeSpan.TryParse(theirs.TimeEnd, out var eb) ? eb : b.Add(TimeSpan.FromMinutes(95));
        if (a >= endB || b >= endA) return 0;
        var mineBuilding = CanonBuilding(mine.BuildingRaw);
        var theirBuilding = CanonBuilding(theirs.BuildingRaw);
        var sameBuilding = mineBuilding is not null && theirBuilding is not null
            && mineBuilding.Equals(theirBuilding, StringComparison.OrdinalIgnoreCase);
        var buildingsConflict = mineBuilding is not null && theirBuilding is not null && !sameBuilding;
        if (SameNonEmpty(mine.RoomRaw, theirs.RoomRaw) && !buildingsConflict) return 100;
        if (!sameBuilding) return 25;
        var floor = Floor(mine.RoomRaw);
        return floor != 0 && floor == Floor(theirs.RoomRaw) ? 75 : 50;
    }

    public static IReadOnlyList<FriendPresence> ForLesson(ScheduleSnapshot mine, Lesson lesson, DateTime date,
        IEnumerable<FriendSchedule> friends, int strictness = 0, bool alwaysShow = false, bool invert = false)
    {
        var result = new List<FriendPresence>();
        foreach (var item in friends.Where(f => f.Friend.Enabled).Take(5))
        {
            var snapshot = item.Snapshot;
            var compatible = snapshot is not null && snapshot.PeriodStart.Date == mine.PeriodStart.Date
                && snapshot.WeekCount == mine.WeekCount && snapshot.SnapshotId == mine.SnapshotId;
            var groupId = string.IsNullOrWhiteSpace(item.Friend.GroupId)
                ? snapshot?.Groups.FirstOrDefault(g => g.Name == item.Friend.GroupName)?.Id
                    ?? snapshot?.Groups.FirstOrDefault(g => g.Id == item.Friend.GroupName)?.Id
                : item.Friend.GroupId;
            compatible &= !string.IsNullOrEmpty(groupId);
            var score = compatible ? ScheduleRules.ForDate(snapshot!, groupId!, date, invert).Select(l => Score(lesson, l)).DefaultIfEmpty(0).Max() : 0;
            var present = score > 0 && score >= strictness;
            if (present || alwaysShow)
                result.Add(new(item.Friend, score, present, present ? Label(score) : "нет на месте", compatible));
        }
        return result;
    }

    private static string? CanonBuilding(string? building)
    {
        if (string.IsNullOrWhiteSpace(building)) return null;
        var t = building.Trim();
        if (t.Equals("ВЦ", StringComparison.OrdinalIgnoreCase)
            || t.Equals("ГК", StringComparison.OrdinalIgnoreCase)
            || t.Equals("main", StringComparison.OrdinalIgnoreCase))
            return "ГК";
        if (t.Equals("УЛК", StringComparison.OrdinalIgnoreCase)) return "УЛК";
        return t;
    }

    private static bool SameNonEmpty(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int Floor(string? room)
    {
        if (string.IsNullOrWhiteSpace(room)) return 0;
        var digits = Regex.Match(room, @"\d+").Value;
        return digits.Length > 0 && digits[0] is >= '1' and <= '9' ? digits[0] - '0' : 0;
    }

    private static string Label(int score) => score switch
    {
        100 => "в той же аудитории", 75 => "на том же этаже", 50 => "в том же корпусе", 25 => "в вузе", _ => "нет на месте"
    };
}
