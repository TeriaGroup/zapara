using Vograph.Core.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Vograph.Desktop.Features.Week;

public sealed record AssessmentEntry(DateTime Date, string GroupId, string SubjectRaw, string TypeRaw,
    string TimeStart, string TimeEnd, string TeacherRaw, string ClassroomRaw, string Kind)
{
    public string Label => $"{Date:dd.MM.yyyy} · {TimeStart}–{TimeEnd} · {Kind} · {SubjectRaw} · " +
        $"{TeacherRaw} · {ClassroomRaw}";
    public bool SameLesson(Lesson lesson) => lesson.GroupId == GroupId && lesson.SubjectRaw == SubjectRaw &&
        lesson.TypeRaw == TypeRaw && lesson.TimeStart == TimeStart && lesson.TimeEnd == TimeEnd &&
        lesson.TeacherRaw == TeacherRaw && lesson.ClassroomRaw == ClassroomRaw;
}

public sealed record AssessmentWindow(IReadOnlyList<AssessmentEntry> Items, int UnknownDays);

public static class AssessmentPlanner
{
    private static readonly Regex SubjectPrefix = new(
        @"^(экз\.?|экзамен|зач\.?|зач[её]т|диф\.?\s*зач(?:[её]т)?)\s*[:.\-]?\s",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? Kind(string? typeRaw, string? subjectRaw = null)
    {
        var source = !string.IsNullOrWhiteSpace(typeRaw) ? typeRaw :
            SubjectPrefix.Match(subjectRaw?.Trim() ?? "") is { Success: true } match ? match.Groups[1].Value : "";
        var value = Regex.Replace(source.Trim().ToLowerInvariant().Replace('ё', 'е'), @"[.\s]+", "");
        return value switch { "экз" or "экзамен" => "Экзамен",
            "зач" or "зачет" or "дифзач" or "дифзачет" => "Зачёт", _ => null };
    }

    public static AssessmentWindow Create(string groupId, DateTime start, DateTime? periodStart,
        Func<DateTime, IReadOnlyList<Lesson>> schedule)
    {
        var items = new List<AssessmentEntry>(); var unknown = 0;
        var seen = new HashSet<(int Day, int Parity, int Index, string Start, string End,
            string Subject, string Type, string Teacher, string Room)>();
        for (var offset = 0; offset < 28; offset++)
        {
            var date = start.Date.AddDays(offset);
            if (periodStart is { } period && date < period.Date) { unknown++; continue; }
            foreach (var lesson in schedule(date))
            {
                if (lesson.GroupId != groupId || Kind(lesson.TypeRaw, lesson.SubjectRaw) is not { } kind) continue;
                if (!seen.Add((lesson.DayOfWeek, lesson.Parity, lesson.Index, lesson.TimeStart,
                    lesson.TimeEnd, lesson.SubjectRaw, lesson.TypeRaw, lesson.TeacherRaw, lesson.ClassroomRaw))) continue;
                items.Add(new(date, groupId, lesson.SubjectRaw, lesson.TypeRaw, lesson.TimeStart, lesson.TimeEnd,
                    lesson.TeacherRaw, lesson.ClassroomRaw, kind));
            }
        }
        static TimeSpan OrderTime(string raw) => TimeSpan.TryParse(raw, CultureInfo.InvariantCulture,
            out var time) ? time : TimeSpan.MaxValue;
        return new(items.OrderBy(item => item.Date).ThenBy(item => OrderTime(item.TimeStart))
            .ThenBy(item => item.SubjectRaw, StringComparer.Ordinal).ToArray(), unknown);
    }
}
