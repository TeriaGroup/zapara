using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Week;

namespace Vograph.Desktop.Features.Preferences;

public sealed record StudyImpactResult(DateTime Monday, IReadOnlyList<WeekSlotChange> Changes,
    string Fingerprint, bool Known);

public static class StudyImpactPlanner
{
    public static string Fingerprint(Settings settings, IReadOnlyList<Lesson> lessons,
        IReadOnlyDictionary<string, string> choices)
    {
        var source = JsonSerializer.Serialize(new
        {
            settings.MyGroupId, settings.PeriodStart, settings.WeekCount, settings.ParityInvert,
            Lessons = lessons.Select(lesson => new { lesson.GroupId, lesson.DayOfWeek, lesson.Parity,
                lesson.Index, lesson.TimeStart, lesson.TimeEnd, lesson.SubjectRaw, lesson.TypeRaw,
                lesson.TeacherRaw, lesson.ClassroomRaw }).OrderBy(row => row.DayOfWeek).ThenBy(row => row.Index)
                .ThenBy(row => row.TimeStart).ThenBy(row => row.SubjectRaw).ToArray(),
            Choices = choices.OrderBy(row => row.Key, StringComparer.Ordinal).ToArray()
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    public static StudyImpactResult Preview(Settings settings, IReadOnlyList<Lesson> lessons,
        IReadOnlyDictionary<string, string> choices, DateTime selectedDate, string streamId, string optionId)
    {
        var monday = selectedDate.Date.AddDays(-((int)selectedDate.DayOfWeek + 6) % 7);
        var fingerprint = Fingerprint(settings, lessons, choices);
        if (string.IsNullOrWhiteSpace(settings.MyGroupId) ||
            DateTime.TryParse(settings.PeriodStart, out var period) && monday < period.Date)
            return new(monday, [], fingerprint, false);
        var stream = SubgroupRules.Build(lessons).Streams.FirstOrDefault(row => row.Id == streamId);
        if (stream is null || stream.Options.All(row => row.Id != optionId)) return new(monday, [], fingerprint, false);
        var after = choices.ToDictionary(row => row.Key, row => row.Value);
        if (after.GetValueOrDefault(streamId) == optionId) after.Remove(streamId);
        else after[streamId] = optionId;
        IReadOnlyList<WeekDay> Days(IReadOnlyDictionary<string, string> selected)
        {
            var visible = SubgroupRules.Visible(lessons, selected);
            return Enumerable.Range(0, 7).Select(offset =>
            {
                var date = monday.AddDays(offset);
                var code = ParityService.GetWeekCode(date,
                    DateTime.TryParse(settings.PeriodStart, out var start) ? start : new DateTime(date.Year, 9, 1),
                    settings.WeekCount > 0 ? settings.WeekCount : 2);
                if (settings.ParityInvert) code = code == 1 ? 2 : 1;
                var rows = visible.Where(lesson => lesson.DayOfWeek == offset + 1 &&
                    (lesson.Parity == 0 || lesson.Parity == code))
                    .Select(lesson => new WeekRow(lesson.TimeStart, lesson.SubjectRaw, lesson.TypeRaw,
                        lesson.ClassroomRaw, lesson.SubjectRaw, lesson.TeacherRaw, lesson.TimeEnd,
                        lesson.ClassroomRaw, lesson.TypeRaw)).ToArray();
                return new WeekDay(offset + 1, "", date, false, rows);
            }).ToArray();
        }
        return new(monday, WeekComparison.Compare(Days(choices), Days(after)), fingerprint, true);
    }
}
