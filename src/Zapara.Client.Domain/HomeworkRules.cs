using Vograph.Core.Services;
using Zapara.Contracts.Sync;

namespace Zapara.Client.Domain;

public static class HomeworkRules
{
    public static DateTime? DueDate(ScheduleSnapshot snapshot, string groupId, HomeworkValue homework, TimeZoneInfo localZone, bool invert = false)
    {
        var created = homework.LegacyCreatedLocalDate?.ToDateTime(TimeOnly.MinValue)
            ?? TimeZoneInfo.ConvertTime(homework.CreatedAtUtc, localZone).Date;
        return DueDate(snapshot, groupId, homework.SubjectRaw, created, homework.TargetNthOccurrence, invert);
    }

    public static DateTime? DueDate(ScheduleSnapshot snapshot, string groupId, string subject, DateTime created, int nth, bool invert = false) =>
        ScheduleRules.NextOccurrence(snapshot, groupId, subject, created, Math.Clamp(nth, 1, 10), invert);

    public static string Status(ScheduleSnapshot snapshot, string groupId, string subject, DateTime? due, bool done, DateTime today, bool invert = false)
    {
        if (done) return "done";
        if (due is null || string.IsNullOrEmpty(groupId)) return "pending";
        var days = (due.Value.Date - today.Date).Days;
        if (days < 0) return "overdue";
        if (days == 0) return "burning_urgent";
        if (days == 1) return "burning";
        var lessonsBefore = 0;
        for (var offset = 1; offset <= 120 && offset < days; offset++)
            lessonsBefore += ScheduleRules.ForDate(snapshot, groupId, today.Date.AddDays(offset), invert)
                .Count(l => ParityService.SameSubject(l.SubjectNormalized, subject));
        if (lessonsBefore == 1 || lessonsBefore == 0 && days <= 3) return "approaching";
        return "far";
    }
}
