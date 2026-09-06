using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Summary;

public sealed record CountItem(string Name, int Count);

/// <param name="Parity">1 odd, 2 even, 0 both — as the user sees it.</param>
public sealed record SummaryModel(int Parity, bool IsOddToday, bool HasGroup, int Total,
    IReadOnlyList<CountItem> ByDay, IReadOnlyList<CountItem> ByType, IReadOnlyList<CountItem> Subjects,
    IReadOnlyList<CountItem> Teachers, IReadOnlyList<CountItem> Rooms);

/// <summary>Aggregates of the whole group timetable (WPF CreateSummarySection / Android buildSummary). DB-bound: call under RunAsync.</summary>
public sealed class SummaryComposer
{
    private static readonly string[] DayShortKeys = { "monShort", "tueShort", "wedShort", "thuShort", "friShort", "satShort" };
    private readonly AppServices _app;

    public SummaryComposer(AppServices app) => _app = app;

    /// <param name="parity">null = the week today belongs to; 1 odd; 2 even; 0 both weeks.</param>
    public SummaryModel Compose(int? parity, DateTime today)
    {
        var settings = _app.Settings;
        var periodStart = DateTime.TryParse(settings.PeriodStart, out var ps) ? ps : new DateTime(today.Year, 9, 1);
        var weekCount = settings.WeekCount > 0 ? settings.WeekCount : 2;
        var isOddToday = ParityService.IsOddWeek(today, periodStart, weekCount, settings.ParityInvert);
        var p = parity ?? (isOddToday ? 1 : 2);
        if (string.IsNullOrEmpty(settings.MyGroupId))
            return new SummaryModel(p, isOddToday, false, 0, Array.Empty<CountItem>(), Array.Empty<CountItem>(), Array.Empty<CountItem>(), Array.Empty<CountItem>(), Array.Empty<CountItem>());

        var all = _app.Db.GetAllLessonsForGroup(settings.MyGroupId);
        var xmlParity = settings.ParityInvert ? (p == 1 ? 2 : 1) : p; // cache keeps XML week codes
        var lessons = p == 0 ? all : all.Where(l => l.Parity == xmlParity).ToList();
        return Build(p, isOddToday, lessons, l => ScheduleComposer.StripType(_app.Overrides.GetDisplayName(l.SubjectRaw, l.DayOfWeek), l.TypeRaw), _app.Loc);
    }

    public static SummaryModel Build(int parity, bool isOddToday, IReadOnlyList<Lesson> lessons, Func<Lesson, string> displayName, Loc loc)
    {
        var byDay = Enumerable.Range(1, 6).Select(d => new CountItem(loc.T(DayShortKeys[d - 1]), lessons.Count(l => l.DayOfWeek == d))).ToList();
        var byType = Counts(lessons, l => string.IsNullOrWhiteSpace(l.TypeRaw) ? "—" : DayTitles.TypeLabel(l.TypeRaw, loc));
        var subjects = Counts(lessons, l => string.IsNullOrWhiteSpace(l.SubjectRaw) ? "—" : displayName(l));
        var teachers = Counts(lessons.Where(l => !string.IsNullOrWhiteSpace(l.TeacherRaw) && l.TeacherRaw != "—")
            .SelectMany(l => l.TeacherRaw.Split(';').Select(t => t.Trim()).Where(t => t.Length > 0)), t => t);
        var rooms = Counts(lessons.Select(l => l.ClassroomRaw.TrimEnd(';', ' ')).Where(r => r.Length > 0), r => r);
        return new SummaryModel(parity, isOddToday, true, lessons.Count, byDay, byType, subjects, teachers, rooms);
    }

    /// <summary>Most frequent first, ties by name (culture-aware, so «Матан» sorts after Latin).</summary>
    private static List<CountItem> Counts<T>(IEnumerable<T> items, Func<T, string> key) =>
        items.GroupBy(key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CountItem(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true))
            .ToList();
}
