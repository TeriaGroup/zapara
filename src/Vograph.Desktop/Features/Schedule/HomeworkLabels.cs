using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Schedule;

public static class HomeworkLabels
{
    /// <summary>Lessons of the subject strictly between today and the due date (mirrors HomeworkService.ComputeStatus).</summary>
    public static int LessonsUntil(Database db, Settings settings, string subjectNormalized, DateTime today, DateTime due)
    {
        if (string.IsNullOrEmpty(settings.MyGroupId)) return 0;
        var periodStart = DateTime.TryParse(settings.PeriodStart, out var ps) ? ps : new DateTime(today.Year, 9, 1);
        var weekCount = settings.WeekCount > 0 ? settings.WeekCount : 2;
        var count = 0;
        for (var d = today.Date.AddDays(1); d < due.Date; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Sunday) continue;
            var weekCode = ParityService.GetWeekCode(d, periodStart, weekCount);
            if (settings.ParityInvert) weekCode = weekCode == 1 ? 2 : 1;
            count += db.GetLessons(settings.MyGroupId, (int)d.DayOfWeek, weekCode).Count(l => ParityService.NormalizeSubject(l.SubjectRaw) == subjectNormalized);
        }
        return count;
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

    /// <summary>CSS-like class for the homework block: far | approaching | burning | urgent | overdue | done.</summary>
    public static string StatusClass(string status) => status switch
    {
        "approaching" => "approaching",
        "burning" => "burning",
        "burning_urgent" => "urgent",
        "overdue" => "overdue",
        "done" => "done",
        _ => "far"
    };
}
