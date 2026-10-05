namespace Vograph.Desktop.Features.Schedule;

public enum LessonTypeBadgeKind { Unknown, Lecture, Practice, Lab, Consult, Credit, Exam, Course }

public static class LessonTypeBadge
{
    public static LessonTypeBadgeKind KindOf(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "лек" or "лекция" => LessonTypeBadgeKind.Lecture,
        "пр" or "практика" => LessonTypeBadgeKind.Practice,
        "лаб" or "лабораторная" or "лабораторная работа" => LessonTypeBadgeKind.Lab,
        "конс" or "консультация" => LessonTypeBadgeKind.Consult,
        "зач" or "зачёт" or "зачет" => LessonTypeBadgeKind.Credit,
        "экз" or "экзамен" => LessonTypeBadgeKind.Exam,
        "курс" or "курсовая" or "курсовая работа" => LessonTypeBadgeKind.Course,
        _ => LessonTypeBadgeKind.Unknown
    };
}
