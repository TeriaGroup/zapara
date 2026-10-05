using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Domain;

/// <summary>Friend dots for one lesson — shared by the day cards and the Friends preview. DB-bound (IntersectionService reads the friends' timetables): call under RunAsync.</summary>
public static class FriendMarks
{
    public sealed record DaySchedule(FriendGroup Friend, IReadOnlyList<Lesson>? Lessons);

    /// <summary>Read each group's compatible day once, before composing individual cards.</summary>
    public static IReadOnlyList<DaySchedule> PrepareDay(Database db, IntersectionService intersections,
        string selectedGroupId, DateTime date, IReadOnlyList<FriendGroup> friends, Settings settings)
    {
        var groups = db.GetAllGroups();
        var comparable = intersections.ComparableFriends(selectedGroupId, friends).Select(f => f.Id).ToHashSet();
        var period = ParityCodes.Period(settings, date);
        var parity = ParityService.GetWeekCode(date, period.PeriodStart, period.WeekCount);
        if (settings.ParityInvert) parity = parity == 1 ? 2 : 1;
        var result = new List<DaySchedule>();
        foreach (var friend in friends.Where(f => f.Enabled).Take(5))
        {
            var group = groups.FirstOrDefault(g => g.Name.Equals(friend.GroupName, StringComparison.OrdinalIgnoreCase)
                || g.Id.Equals(friend.GroupName, StringComparison.OrdinalIgnoreCase));
            if (group?.Id == selectedGroupId) continue;
            IReadOnlyList<Lesson>? lessons = comparable.Contains(friend.Id) && group is not null
                ? date.DayOfWeek == DayOfWeek.Sunday ? [] : db.GetLessons(group.Id, (int)date.DayOfWeek, parity)
                : null;
            result.Add(new(friend, lessons));
        }
        return result;
    }

    public static IReadOnlyList<FriendMark> Compute(Lesson lesson, IReadOnlyList<DaySchedule> schedules,
        Settings settings, Loc loc, bool upcoming)
    {
        var marks = new List<FriendMark>();
        foreach (var schedule in schedules)
        {
            var friend = schedule.Friend;
            var best = schedule.Lessons?.Where(other => IntersectionService.TimesOverlap(lesson.TimeStart, lesson.TimeEnd, other.TimeStart, other.TimeEnd))
                .Select(other => IntersectionService.PlaceScore(lesson, other)).DefaultIfEmpty(0).Max() ?? 0;
            var hasLesson = HasLesson(lesson, schedule.Lessons);
            var present = best > 0 && best >= settings.IntersectionStrictness;
            if (!upcoming && (schedule.Lessons is null || !present && !settings.AlwaysShowAllTrafficLights)) continue;
            var place = loc.T(best switch { >= 100 => "inter100", >= 75 => "inter75", >= 50 => "inter50", _ => "inter25" });
            var where = hasLesson is null ? "Нет данных" : present ? place
                : best > 0 ? $"{place} · ниже выбранной точности" : loc.T("friendAbsent");
            var names = string.IsNullOrWhiteSpace(friend.MemberNames) ? "" : $" ({friend.MemberNames})";
            marks.Add(new(friend.GroupName, friend.MemberNames, FriendPalette.IndexOf(friend.ColorHex),
                present ? FriendDot.FromScore(best) : DotFill.Off, $"{friend.GroupName}{names} · {where}", hasLesson, upcoming));
        }
        return marks;
    }

    internal static bool? HasLesson(Lesson mine, IReadOnlyList<Lesson>? lessons)
    {
        if (lessons is null || !TryInterval(mine, out var start, out var end)) return null;
        var incomplete = false;
        foreach (var other in lessons)
        {
            if (!TryInterval(other, out var otherStart, out var otherEnd)) { incomplete = true; continue; }
            if (start < otherEnd && otherStart < end) return true;
        }
        return incomplete ? null : false;
    }

    private static bool TryInterval(Lesson lesson, out TimeSpan start, out TimeSpan end)
    {
        end = default;
        if (!TimeSpan.TryParse(lesson.TimeStart, out start) || start < TimeSpan.Zero || start >= TimeSpan.FromDays(1)) return false;
        if (string.IsNullOrWhiteSpace(lesson.TimeEnd)) end = start.Add(TimeSpan.FromMinutes(95));
        else if (!TimeSpan.TryParse(lesson.TimeEnd, out end)) return false;
        return end > start && end <= TimeSpan.FromDays(1);
    }

    public static IReadOnlyList<FriendMark> Compute(IntersectionService intersections, Lesson l, DateTime date, IReadOnlyList<FriendGroup> friends, Settings settings, Loc loc)
    {
        var enabled = intersections.ComparableFriends(l.GroupId, friends);
        if (enabled.Count == 0) return Array.Empty<FriendMark>();
        // strictness 0 → every time overlap; the visibility threshold is applied below.
        var results = intersections.GetIntersections(l, date, enabled, strictness: 0);
        return Compute(enabled, results, settings, loc);
    }

    /// <summary>Formats dots from an already-computed overlap snapshot, shared by the Friends forecast.</summary>
    public static IReadOnlyList<FriendMark> Compute(IReadOnlyList<FriendGroup> enabled,
        IReadOnlyList<IntersectionService.IntersectionResult> results, Settings settings, Loc loc)
    {
        var marks = new List<FriendMark>();
        foreach (var f in enabled)
        {
            var best = results.Where(r => r.FriendGroupName == f.GroupName).Select(r => r.Score).DefaultIfEmpty(0).Max();
            var present = best > 0 && best >= settings.IntersectionStrictness;
            if (!present && !settings.AlwaysShowAllTrafficLights) continue;
            var place = loc.T(best switch { >= 100 => "inter100", >= 75 => "inter75", >= 50 => "inter50", _ => "inter25" });
            var where = present ? place : best > 0 ? $"{place} · ниже выбранной точности" : loc.T("friendAbsent");
            var names = string.IsNullOrWhiteSpace(f.MemberNames) ? "" : $" ({f.MemberNames})";
            marks.Add(new FriendMark(f.GroupName, f.MemberNames, FriendPalette.IndexOf(f.ColorHex), present ? FriendDot.FromScore(best) : DotFill.Off, $"{f.GroupName}{names} · {where}"));
        }
        return marks;
    }
}
