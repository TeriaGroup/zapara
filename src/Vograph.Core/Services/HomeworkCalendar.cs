using Vograph.Core.Models;

namespace Vograph.Core.Services;

/// <summary>Due dates and the next meeting of a subject, counted only on the chosen subgroup.</summary>
public static class HomeworkCalendar
{
    public static DateTime? DueDate(
        IReadOnlyList<Lesson> lessons,
        IReadOnlyDictionary<string, string> choices,
        DateTime periodStart,
        int weekCount,
        bool invert,
        string subjectNormalized,
        DateTime from,
        int nth)
    {
        if (nth < 1 || string.IsNullOrEmpty(subjectNormalized)) return null;
        var visible = SubgroupRules.Visible(lessons, choices);
        var found = 0;
        var weeks = weekCount > 0 ? weekCount : 2;
        for (var offset = 1; offset <= 120; offset++)
        {
            var date = from.Date.AddDays(offset);
            if (date.DayOfWeek == DayOfWeek.Sunday) continue;
            if (!Meets(visible, date, periodStart, weeks, invert, subjectNormalized)) continue;
            found++;
            if (found == nth) return date;
        }
        return null;
    }

    public static DateTime? NextDate(
        IReadOnlyList<Lesson> lessons,
        IReadOnlyDictionary<string, string> choices,
        DateTime periodStart,
        int weekCount,
        bool invert,
        string subjectNormalized,
        DateTime from,
        int maxDays = 60)
    {
        if (string.IsNullOrEmpty(subjectNormalized)) return null;
        var visible = SubgroupRules.Visible(lessons, choices);
        var weeks = weekCount > 0 ? weekCount : 2;
        for (var offset = 1; offset <= maxDays; offset++)
        {
            var date = from.Date.AddDays(offset);
            if (date.DayOfWeek == DayOfWeek.Sunday) continue;
            if (Meets(visible, date, periodStart, weeks, invert, subjectNormalized)) return date;
        }
        return null;
    }

    public static int MeetingsBetween(
        IReadOnlyList<Lesson> lessons,
        IReadOnlyDictionary<string, string> choices,
        DateTime periodStart,
        int weekCount,
        bool invert,
        string subjectNormalized,
        DateTime today,
        DateTime due,
        int maxDays = int.MaxValue)
    {
        var visible = SubgroupRules.Visible(lessons, choices);
        var weeks = weekCount > 0 ? weekCount : 2;
        var count = 0;
        var steps = 0;
        for (var date = today.Date.AddDays(1); date < due.Date && steps < maxDays; date = date.AddDays(1), steps++)
        {
            if (date.DayOfWeek == DayOfWeek.Sunday) continue;
            count += Meetings(visible, date, periodStart, weeks, invert, subjectNormalized);
        }
        return count;
    }

    private static bool Meets(IReadOnlyList<Lesson> visible, DateTime date, DateTime periodStart, int weekCount, bool invert, string subjectNormalized) =>
        Meetings(visible, date, periodStart, weekCount, invert, subjectNormalized) > 0;

    private static int Meetings(IReadOnlyList<Lesson> visible, DateTime date, DateTime periodStart, int weekCount, bool invert, string subjectNormalized)
    {
        var dow = (int)date.DayOfWeek;
        if (dow == 0) dow = 7;
        var code = ParityService.GetWeekCode(date, periodStart, weekCount);
        if (invert) code = code == 1 ? 2 : 1;
        var count = 0;
        foreach (var lesson in visible)
        {
            if (lesson.DayOfWeek != dow || (lesson.Parity != 0 && lesson.Parity != code)) continue;
            if (ParityService.SameSubject(lesson.SubjectNormalized, subjectNormalized)) count++;
        }
        return count;
    }
}
