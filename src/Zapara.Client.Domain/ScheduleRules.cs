using System.Runtime.CompilerServices;
using Vograph.Core.Models;
using Vograph.Core.Services;

namespace Zapara.Client.Domain;

public sealed record ScheduleSnapshot(DateTime PeriodStart, int WeekCount,
    IReadOnlyList<Group> Groups, IReadOnlyList<Lesson> Lessons,
    string? SnapshotId = null, DateTimeOffset? FetchedAt = null);

public static class ScheduleRules
{
    private static readonly ConditionalWeakTable<ScheduleSnapshot, Dictionary<(string Group, int Day), Lesson[]>> days = new();

    public static int WeekCode(ScheduleSnapshot snapshot, DateTime date, bool invert = false)
    {
        var code = ParityService.GetWeekCode(date, snapshot.PeriodStart, snapshot.WeekCount > 0 ? snapshot.WeekCount : 2);
        return invert ? code == 1 ? 2 : 1 : code;
    }

    public static IReadOnlyList<Lesson> ForDate(ScheduleSnapshot snapshot, string groupId, DateTime date, bool invert = false)
    {
        if (string.IsNullOrEmpty(groupId) || date.DayOfWeek == DayOfWeek.Sunday) return [];
        var parity = WeekCode(snapshot, date, invert);
        if (!days.GetValue(snapshot, Index).TryGetValue((groupId, (int)date.DayOfWeek), out var list)) return [];
        if (list.All(lesson => lesson.Parity == 0 || lesson.Parity == parity)) return list;
        return list.Where(lesson => lesson.Parity == 0 || lesson.Parity == parity).ToArray();
    }

    private static Dictionary<(string Group, int Day), Lesson[]> Index(ScheduleSnapshot snapshot)
    {
        var grouped = new Dictionary<(string, int), List<Lesson>>();
        foreach (var lesson in snapshot.Lessons)
        {
            var key = (lesson.GroupId ?? "", lesson.DayOfWeek);
            if (!grouped.TryGetValue(key, out var list)) grouped[key] = list = [];
            list.Add(lesson);
        }
        var result = new Dictionary<(string, int), Lesson[]>(grouped.Count);
        foreach (var pair in grouped)
            result[pair.Key] = pair.Value
                .OrderBy(lesson => TimeSpan.TryParse(lesson.TimeStart, out var time) ? time : TimeSpan.MaxValue)
                .ThenBy(lesson => lesson.Index)
                .ToArray();
        return result;
    }

    public static DateTime SmartStart(ScheduleSnapshot snapshot, string groupId, DateTime now, bool invert = false)
    {
        if (now.DayOfWeek != DayOfWeek.Sunday && ForDate(snapshot, groupId, now, invert)
            .Any(l => TimeSpan.TryParse(l.TimeEnd, out var end) && end > now.TimeOfDay)) return now.Date;
        return now.Date.AddDays(now.DayOfWeek == DayOfWeek.Saturday ? 2 : 1);
    }

    public static DateTime? NextOccurrence(ScheduleSnapshot snapshot, string groupId, string subject, DateTime after,
        int nth = 1, bool invert = false, int maxDays = 120)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(groupId)) return null;
        nth = Math.Clamp(nth, 1, 10);
        var found = 0;
        for (var offset = 1; offset <= maxDays; offset++)
        {
            var date = after.Date.AddDays(offset);
            if (ForDate(snapshot, groupId, date, invert).Any(l => ParityService.SameSubject(l.SubjectNormalized, subject)) && ++found == nth)
                return date;
        }
        return null;
    }
}
