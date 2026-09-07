using Vograph.Core.Models;
using Vograph.Core.Services;

namespace Vograph.Desktop.Domain;

/// <summary>Date of the next lesson of the same (normalized) subject — the old "След." column.</summary>
public static class NextOccurrence
{
    public static DateTime? Find(Database db, Settings settings, string subjectRaw, DateTime fromDate, int maxDays = 60)
    {
        var norm = ParityService.NormalizeSubject(subjectRaw);
        if (norm.Length == 0 || string.IsNullOrEmpty(settings.MyGroupId)) return null;
        var period = ParityCodes.Period(settings, fromDate);
        for (var offset = 1; offset <= maxDays; offset++)
        {
            var date = fromDate.Date.AddDays(offset);
            if (date.DayOfWeek == DayOfWeek.Sunday) continue;
            if (db.GetLessons(settings.MyGroupId, (int)date.DayOfWeek, ParityCodes.WeekCode(date, settings, period)).Any(l => ParityService.NormalizeSubject(l.SubjectRaw) == norm))
                return date;
        }
        return null;
    }
}
