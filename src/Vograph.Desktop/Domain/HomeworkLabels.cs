using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Domain;

public static class HomeworkLabels
{
    /// <summary>Lessons of the subject strictly between today and the due date (mirrors HomeworkService.ComputeStatus).</summary>
    public static int LessonsUntil(Database db, Settings settings, string subjectNormalized, DateTime today, DateTime due)
    {
        if (string.IsNullOrEmpty(settings.MyGroupId)) return 0;
        return LessonsUntil(db.GetAllLessonsForGroup(settings.MyGroupId), db.GetSubgroupChoices(settings.MyGroupId), settings, subjectNormalized, today, due);
    }

    public static int LessonsUntil(IReadOnlyList<Lesson> lessons, IReadOnlyDictionary<string, string> choices, Settings settings, string subjectNormalized, DateTime today, DateTime due)
    {
        var period = ParityCodes.Period(settings, today);
        return HomeworkCalendar.MeetingsBetween(lessons, choices, period.PeriodStart, period.WeekCount, settings.ParityInvert, subjectNormalized, today, due);
    }

    public static string Label(string status, DateTime? due, int lessonsUntil, Loc loc)
    {
        switch (status)
        {
            case "done": return loc.T("hwDone");
            case "overdue": return loc.T("hwOverdue", due is null ? "" : DayTitles.ShortDate(due.Value, loc));
            case "burning_urgent": return loc.T("hwBurningToday");
            case "burning": return loc.T("hwBurningTomorrow");
            default:
                if (due is null) return loc.T("hwNoDate");
                var text = loc.T("hwDueOn", DayTitles.ShortDate(due.Value, loc));
                return lessonsUntil > 0 ? $"{text} · {loc.Plural(lessonsUntil, "hwInLessons1", "hwInLessons2", "hwInLessons5")}" : text;
        }
    }
}
