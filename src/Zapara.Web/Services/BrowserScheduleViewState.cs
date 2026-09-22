using Vograph.Core.Models;
using Vograph.Core.Services;
using System.Globalization;
using Zapara.Client.Domain;
using Zapara.Contracts.Sync;

namespace Zapara.Web.Services;

public sealed class BrowserScheduleViewState
{
    private readonly Dictionary<(string Owner, string Group), DateTime> selected = [];
    public static readonly DateTime MinimumDate = new(1900, 1, 1);
    public static readonly DateTime MaximumDate = new(2100, 12, 31);
    public DateTime Open(string owner, string groupId, ScheduleSnapshot? snapshot, DateTime now, bool invert, string? query = null)
    {
        if (TryDate(query, out var requested)) { Select(owner, groupId, requested); return requested; }
        if (selected.TryGetValue((owner, groupId), out var saved)) return saved;
        if (snapshot is null || string.IsNullOrEmpty(groupId)) return now.Date;
        var date = ScheduleRules.SmartStart(snapshot, groupId, now, invert);
        Select(owner, groupId, date);
        return date;
    }
    public void Select(string owner, string groupId, DateTime date) => selected[(owner, groupId)] = Clamp(date);
    public static DateTime Clamp(DateTime date) => date < MinimumDate ? MinimumDate : date > MaximumDate ? MaximumDate : date.Date;
    public static bool TryDate(string? input, out DateTime date) => DateTime.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out date) && date >= MinimumDate && date <= MaximumDate;
}

public sealed record ScheduleHomeworkIndicator(Guid Id, string Text, string Status, DateTime? Due);
public static class BrowserSchedulePresentation
{
    public static IReadOnlyList<string> Timings(IReadOnlyList<Lesson> lessons, DateTime date, DateTime now)
    {
        var nextAssigned = false;
        return lessons.Select(lesson =>
        {
            if (date.Date != now.Date || !TimeSpan.TryParse(lesson.TimeStart, out var start) || !TimeSpan.TryParse(lesson.TimeEnd, out var end) || end <= start) return "";
            if (end <= now.TimeOfDay) return "past";
            if (start <= now.TimeOfDay) return "current";
            if (nextAssigned) return "";
            nextAssigned = true;
            return "next";
        }).ToArray();
    }
    public static IReadOnlyList<ScheduleHomeworkIndicator> Homework(ScheduleSnapshot snapshot, string groupId, Lesson lesson, DateTime today,
        IEnumerable<EntityValue<HomeworkValue>> homework, IEnumerable<EntityValue<CompletionValue>> completions, bool invert)
    {
        var done = completions.Where(c => c.Value.Done).Select(c => c.Id).ToHashSet();
        return homework.Where(h => ParityService.SameSubject(h.Value.SubjectRaw, lesson.SubjectRaw)).Select(h =>
        {
            var due = HomeworkRules.DueDate(snapshot, groupId, h.Value, TimeZoneInfo.Local, invert);
            return new ScheduleHomeworkIndicator(h.Id, h.Value.Text, HomeworkRules.Status(snapshot, groupId, h.Value.SubjectRaw, due, done.Contains(h.Id), today, invert), due);
        }).OrderBy(h => h.Status switch { "burning_urgent" => 0, "burning" => 1, "overdue" => 2, "approaching" => 3, "done" => 5, _ => 4 }).ThenBy(h => h.Due).ToArray();
    }
    public static IReadOnlyList<FriendPresence> Friends(ScheduleSnapshot snapshot, Lesson lesson, DateTime date,
        IEnumerable<FriendValue> friends, Func<string?, bool> hasGroupData, int strictness, bool alwaysShow, bool invert) =>
        FriendRules.ForLesson(snapshot, lesson, date, friends.Select(friend => new FriendSchedule(friend, hasGroupData(friend.GroupId) ? snapshot : null)), strictness, alwaysShow, invert);
}
