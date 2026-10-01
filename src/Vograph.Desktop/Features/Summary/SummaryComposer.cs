using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Summary;

public sealed record CountItem(string Name, int Count);

/// <param name="Parity">1 odd, 2 even, 0 both — as the user sees it.</param>
public sealed record SummaryModel(int Parity, bool IsOddToday, bool HasGroup, int Total,
    IReadOnlyList<CountItem> ByDay, IReadOnlyList<CountItem> ByType, IReadOnlyList<CountItem> Subjects,
    IReadOnlyList<CountItem> Teachers, IReadOnlyList<CountItem> Rooms,
    bool HasCopy = true, IReadOnlyList<DateTime>? DayDates = null);

/// <summary>Aggregates of the whole group timetable (WPF CreateSummarySection / Android buildSummary). DB-bound: call under RunAsync.</summary>
public sealed class SummaryComposer
{
    private readonly AppServices _app;

    public SummaryComposer(AppServices app) => _app = app;

    /// <param name="parity">null = the week today belongs to; 1 odd; 2 even; 0 both weeks.</param>
    public SummaryModel Compose(int? parity, DateTime today)
    {
        var settings = _app.Settings;
        var currentCode = ParityCodes.WeekCode(today, settings);
        var currentUserParity = ParityCodes.ToUser(currentCode, settings.ParityInvert);
        var isOddToday = currentUserParity == 1;
        var p = parity ?? currentUserParity;
        if (string.IsNullOrEmpty(settings.MyGroupId))
            return new SummaryModel(p, isOddToday, false, 0, Array.Empty<CountItem>(), Array.Empty<CountItem>(), Array.Empty<CountItem>(), Array.Empty<CountItem>(), Array.Empty<CountItem>());

        var group = settings.MyGroupId;
        var hasCopy = !_app.Api.Configured ? _app.Db.GetGroup(group) is not null
            : new TimetableApiCache(_app.Db).Read(group) is { } metadata &&
              (metadata.Source == "api" || metadata.FetchedAt is not null) ||
              _app.Db.GetGroup(group)?.LastFetchedAt is not null || _app.Db.GetAllLessonsForGroup(group).Count > 0;
        if (!hasCopy) return new SummaryModel(p, isOddToday, true, 0, [], [], [], [], [], false);
        var all = _app.Db.GetAllLessonsForGroup(group);
        var code = p == 0 ? 0 : ParityCodes.ToXml(p, settings.ParityInvert);
        var lessons = code == 0 ? all : all.Where(lesson => lesson.Parity == 0 || lesson.Parity == code).ToList();
        var summary = Build(p, isOddToday, lessons, l => LessonText.StripType(_app.Overrides.GetDisplayName(l.SubjectRaw, l.DayOfWeek), l.TypeRaw), _app.Loc);
        var dates = Enumerable.Range(1, 6).Select(dow => p == 0
            ? today.Date.AddDays((dow - ((int)today.DayOfWeek is 0 ? 7 : (int)today.DayOfWeek) + 7) % 7)
            : NextDateForCode(dow, code, today, settings)).ToArray();
        return summary with { DayDates = dates };
    }

    private static DateTime NextDateForCode(int dow, int code, DateTime today, Settings settings)
    {
        var period = ParityCodes.Period(settings, today);
        for (var offset = 0; offset < 56; offset++)
        {
            var date = today.Date.AddDays(offset);
            if ((int)date.DayOfWeek == dow && ParityCodes.WeekCode(date, settings, period) == code) return date;
        }
        return today.Date;
    }

    public static List<Lesson> ForUserParity(IReadOnlyList<Lesson> all, int userParity, bool invert)
    {
        if (userParity == 0) return all.ToList();
        var xmlParity = ParityCodes.ToXml(userParity, invert);
        return all.Where(lesson => lesson.Parity == 0 || lesson.Parity == xmlParity).ToList();
    }

    public static SummaryModel Build(int parity, bool isOddToday, IReadOnlyList<Lesson> lessons, Func<Lesson, string> displayName, Loc loc)
    {
        var byDay = Enumerable.Range(1, 6).Select(d => new CountItem(loc.T(DayNames.ShortKey(d)), lessons.Count(l => l.DayOfWeek == d))).ToList();
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
