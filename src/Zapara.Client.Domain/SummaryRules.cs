using Vograph.Core.Models;
using Vograph.Core.Services;
using System.Globalization;

namespace Zapara.Client.Domain;

public sealed record CountItem(string Name, int Count);
public sealed record ScheduleSummary(int Parity, int Total, IReadOnlyList<CountItem> ByDay,
    IReadOnlyList<CountItem> ByType, IReadOnlyList<CountItem> Subjects, IReadOnlyList<CountItem> Teachers, IReadOnlyList<CountItem> Rooms);

public static class SummaryRules
{
    public static ScheduleSummary Compose(ScheduleSnapshot snapshot, string groupId, int parity = 0, bool invert = false,
        Func<Lesson, string>? displayName = null)
    {
        var code = invert && parity != 0 ? parity == 1 ? 2 : 1 : parity;
        var lessons = snapshot.Lessons.Where(l => l.GroupId == groupId && (code == 0 || l.Parity == 0 || l.Parity == code)).ToArray();
        return new(parity, lessons.Length,
            Enumerable.Range(1, 6).Select(d => new CountItem(ParityService.DayNumberToTitle(d), lessons.Count(l => l.DayOfWeek == d))).ToArray(),
            Counts(lessons.Select(l => TypeLabel(l.TypeRaw))),
            Counts(lessons.Select(l => string.IsNullOrWhiteSpace(l.SubjectRaw) ? "—" : displayName?.Invoke(l) ?? StripType(l))),
            Counts(lessons.SelectMany(l => (l.TeacherRaw ?? "").Split(';')).Select(t => t.Trim()).Where(t => t.Length > 0 && t != "—")),
            Counts(lessons.Select(l => (l.ClassroomRaw ?? "").TrimEnd(';', ' ')).Where(r => r.Length > 0)));
    }

    private static string StripType(Lesson lesson)
    {
        var type = lesson.TypeRaw.Trim();
        return type.Length > 0 && lesson.SubjectRaw.StartsWith(type + " ", StringComparison.OrdinalIgnoreCase)
            ? lesson.SubjectRaw[(type.Length + 1)..].Trim() : lesson.SubjectRaw;
    }

    private static string TypeLabel(string value) => value.Trim().ToLowerInvariant() switch
    {
        "лек" => "лекция", "пр" or "практика" => "практика", "лаб" => "лабораторная", "конс" => "консультация",
        "зач" => "зачёт", "экз" => "экзамен", "курс" => "курсовая", "" => "—", var other => other
    };

    private static IReadOnlyList<CountItem> Counts(IEnumerable<string> names) => names.GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
        .Select(g => new CountItem(g.Key, g.Count())).OrderByDescending(g => g.Count)
        .ThenBy(g => g.Name, StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), true)).ToArray();
}
