using Vograph.Core.Models;
using Vograph.Core.Services;

namespace Vograph.Desktop.Domain;

/// <summary>Date of the next lesson of the same (normalized) subject — the old "След." column.</summary>
public static class NextOccurrence
{
    public static DateTime? Find(Database db, Settings settings, string subjectRaw, DateTime fromDate, int maxDays = 60)
    {
        if (string.IsNullOrEmpty(settings.MyGroupId)) return null;
        return Find(db.GetAllLessonsForGroup(settings.MyGroupId), db.GetSubgroupChoices(settings.MyGroupId), settings, subjectRaw, fromDate, maxDays);
    }

    public static DateTime? Find(IReadOnlyList<Lesson> lessons, IReadOnlyDictionary<string, string> choices, Settings settings, string subjectRaw, DateTime fromDate, int maxDays = 60)
    {
        var norm = ParityService.NormalizeSubject(subjectRaw);
        if (norm.Length == 0 || string.IsNullOrEmpty(settings.MyGroupId)) return null;
        var period = ParityCodes.Period(settings, fromDate);
        return HomeworkCalendar.NextDate(lessons, choices, period.PeriodStart, period.WeekCount, settings.ParityInvert, norm, fromDate, maxDays);
    }
}
