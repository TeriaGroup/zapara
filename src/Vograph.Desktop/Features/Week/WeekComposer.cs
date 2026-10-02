using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Week;

public sealed record WeekRow(string Time, string Name, string TypeLabel, string Room,
    string SubjectRaw = "", string Teacher = "", string TimeEnd = "", string ClassroomRaw = "", string TypeRaw = "");
public sealed record WeekDeadline(long Id, DateTime Due, string Subject, string Text, bool Done);
public sealed record WeekDay(int Dow, string Title, DateTime Date, bool IsToday, IReadOnlyList<WeekRow> Rows,
    IReadOnlyList<WeekDeadline>? Deadlines = null);
/// <param name="Parity">1 odd / 2 even as the user sees it (inversion already applied).</param>
public sealed record WeekModel(int Parity, bool IsOddToday, bool HasGroup, int Total, IReadOnlyList<WeekDay> Days,
    bool HasCopy = true, DateTime? WeekStart = null, IReadOnlyList<WeekDeadline>? UnknownDeadlines = null);

/// <summary>Six day cards of one parity. Synchronous and DB-bound — call from ViewModelBase.RunAsync.</summary>
public sealed class WeekComposer
{
    private readonly AppServices _app;

    public WeekComposer(AppServices app) => _app = app;

    public WeekModel ComposeCalendar(DateTime selectedDate, DateTime today)
    {
        var settings = _app.Settings;
        var monday = selectedDate.Date.AddDays(-((int)selectedDate.DayOfWeek + 6) % 7);
        var odd = ParityCodes.ToUser(ParityCodes.WeekCode(monday, settings), settings.ParityInvert) == 1;
        var parity = odd ? 1 : 2;
        var todayOdd = ParityCodes.ToUser(ParityCodes.WeekCode(today, settings), settings.ParityInvert) == 1;
        if (string.IsNullOrEmpty(settings.MyGroupId))
            return new WeekModel(parity, todayOdd, false, 0, [], false, monday);
        var group = settings.MyGroupId;
        var hasCopy = !_app.Api.Configured ? _app.Db.GetGroup(group) is not null
            : new TimetableApiCache(_app.Db).Read(group) is { } metadata &&
              (metadata.Source == "api" || metadata.FetchedAt is not null) ||
              _app.Db.GetGroup(group)?.LastFetchedAt is not null || _app.Db.GetAllLessonsForGroup(group).Count > 0;
        if (!hasCopy) return new WeekModel(parity, todayOdd, true, 0, [], false, monday);
        var personal = _app.Homework.GetAll()
            .Select(task => new WeekDeadline(task.Id,
                (task.Status == "done" ? task.DueDateComputed : _app.Homework.ComputeDueDate(
                    task.SubjectRawNormalized, task.CreatedAt, task.TargetNthOccurrence))?.Date ?? DateTime.MinValue,
                HomeworkComposer.OrphanDisplay(task.SubjectRawNormalized), task.Text, task.Status == "done"))
            .ToArray();
        var deadlines = personal.Where(task => task.Due != DateTime.MinValue)
            .GroupBy(task => task.Due).ToDictionary(grouping => grouping.Key, grouping => (IReadOnlyList<WeekDeadline>)grouping.ToArray());
        var days = Enumerable.Range(0, 7).Select(offset =>
        {
            var date = monday.AddDays(offset);
            List<Lesson> lessons = DateTime.TryParse(settings.PeriodStart, out var start) && date < start.Date
                ? [] : _app.Schedule.GetSchedule(date, group);
            var rows = lessons.OrderBy(l => TimeSpan.TryParse(l.TimeStart, out var t) ? t : TimeSpan.Zero)
                .Select(l => new WeekRow(l.TimeStart,
                    LessonText.StripType(_app.Overrides.GetDisplayName(l.SubjectRaw, l.DayOfWeek), l.TypeRaw),
                    DayTitles.TypeLabel(l.TypeRaw, _app.Loc), RoomLabel(l, _app.Loc), l.SubjectRaw, l.TeacherRaw, l.TimeEnd,
                    l.ClassroomRaw, l.TypeRaw)).ToArray();
            return new WeekDay(offset + 1, _app.Loc.T(DayNames.Key(offset + 1)), date, date == today.Date, rows,
                deadlines.GetValueOrDefault(date) ?? []);
        }).ToArray();
        return new WeekModel(parity, todayOdd, true, days.Sum(day => day.Rows.Count), days, true, monday,
            personal.Where(task => task.Due == DateTime.MinValue).ToArray());
    }

    /// <param name="parity">0 = the week today belongs to; 1 = odd; 2 = even (user-facing).</param>
    public WeekModel Compose(int parity, DateTime today)
    {
        var settings = _app.Settings;
        var loc = _app.Loc;
        var isOddToday = ParityCodes.IsOdd(today, settings);
        if (parity == 0) parity = isOddToday ? 1 : 2;
        if (string.IsNullOrEmpty(settings.MyGroupId)) return new WeekModel(parity, isOddToday, false, 0, Array.Empty<WeekDay>());

        // schedule_cache stores XML week codes; under inversion the user's "odd" is the XML even week.
        var weekCode = ParityCodes.ToXml(parity, settings.ParityInvert);
        var days = new List<WeekDay>(6);
        var total = 0;
        for (var dow = 1; dow <= 6; dow++)
        {
            var date = NearestDate(dow, parity, today, settings);
            var rows = _app.Db.GetLessons(settings.MyGroupId, dow, weekCode)
                .OrderBy(l => TimeSpan.TryParse(l.TimeStart, out var t) ? t : TimeSpan.Zero)
                .Select(l => new WeekRow(
                    l.TimeStart,
                    LessonText.StripType(_app.Overrides.GetDisplayName(l.SubjectRaw, l.DayOfWeek), l.TypeRaw),
                    DayTitles.TypeLabel(l.TypeRaw, loc),
                    RoomLabel(l, loc), l.SubjectRaw, l.TeacherRaw, l.TimeEnd, l.ClassroomRaw, l.TypeRaw))
                .ToList();
            total += rows.Count;
            days.Add(new WeekDay(dow, loc.T(DayNames.Key(dow)), date, date == today.Date, rows));
        }
        return new WeekModel(parity, isOddToday, true, total, days);
    }

    /// <summary>The first date ≥ today on this weekday whose user-facing parity matches (a two-week cycle always hits within 14 days).</summary>
    public static DateTime NearestDate(int dow, int parity, DateTime today, Settings settings)
    {
        var period = ParityCodes.Period(settings, today);
        for (var i = 0; i < 14; i++)
        {
            var d = today.Date.AddDays(i);
            if ((int)d.DayOfWeek != dow) continue;
            if (ParityCodes.IsOdd(d, settings, period) == (parity == 1)) return d;
        }
        return today.Date;
    }

    private string RoomLabel(Lesson l, Loc loc)
    {
        var (room, tag, _) = LessonText.RoomParts(l, _app.Maps.Resolve(l.ClassroomRaw), loc);
        return tag is null ? room : $"{room} {tag}";
    }
}
