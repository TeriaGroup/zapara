using Vograph.Core.Models;
using Vograph.Core.Services;

namespace Vograph.Desktop.Features.Schedule;

/// <summary>Date of the next lesson of the same (normalized) subject — the old "След." column.</summary>
public static class NextOccurrence
{
    public static DateTime? Find(Database db, Settings settings, string subjectRaw, DateTime fromDate, int maxDays = 60)
    {
        var norm = ParityService.NormalizeSubject(subjectRaw);
        if (norm.Length == 0 || string.IsNullOrEmpty(settings.MyGroupId)) return null;
        var periodStart = DateTime.TryParse(settings.PeriodStart, out var ps) ? ps : new DateTime(fromDate.Year, 9, 1);
        var weekCount = settings.WeekCount > 0 ? settings.WeekCount : 2;

        for (var offset = 1; offset <= maxDays; offset++)
        {
            var date = fromDate.Date.AddDays(offset);
            if (date.DayOfWeek == DayOfWeek.Sunday) continue;
            var weekCode = ParityService.GetWeekCode(date, periodStart, weekCount);
            if (settings.ParityInvert) weekCode = weekCode == 1 ? 2 : 1;
            if (db.GetLessons(settings.MyGroupId, (int)date.DayOfWeek, weekCode).Any(l => ParityService.NormalizeSubject(l.SubjectRaw) == norm))
                return date;
        }
        return null;
    }
}
