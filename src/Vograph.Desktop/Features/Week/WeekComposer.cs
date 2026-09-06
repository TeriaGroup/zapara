using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Week;

public sealed record WeekRow(string Time, string Name, string TypeLabel, string Room);
public sealed record WeekDay(int Dow, string Title, DateTime Date, bool IsToday, IReadOnlyList<WeekRow> Rows);
/// <param name="Parity">1 odd / 2 even as the user sees it (inversion already applied).</param>
public sealed record WeekModel(int Parity, bool IsOddToday, bool HasGroup, int Total, IReadOnlyList<WeekDay> Days);

/// <summary>Six day cards of one parity. Synchronous and DB-bound — call from ViewModelBase.RunAsync.</summary>
public sealed class WeekComposer
{
    private static readonly string[] DayKeys = { "mon", "tue", "wed", "thu", "fri", "sat" };
    private readonly AppServices _app;

    public WeekComposer(AppServices app) => _app = app;

    /// <param name="parity">0 = the week today belongs to; 1 = odd; 2 = even (user-facing).</param>
    public WeekModel Compose(int parity, DateTime today)
    {
        var settings = _app.Settings;
        var loc = _app.Loc;
        var (periodStart, weekCount) = Period(settings, today);
        var isOddToday = ParityService.IsOddWeek(today, periodStart, weekCount, settings.ParityInvert);
        if (parity == 0) parity = isOddToday ? 1 : 2;
        if (string.IsNullOrEmpty(settings.MyGroupId)) return new WeekModel(parity, isOddToday, false, 0, Array.Empty<WeekDay>());

        // schedule_cache stores XML week codes; under inversion the user's "odd" is the XML even week.
        var weekCode = settings.ParityInvert ? (parity == 1 ? 2 : 1) : parity;
        var days = new List<WeekDay>(6);
        var total = 0;
        for (var dow = 1; dow <= 6; dow++)
        {
            var date = NearestDate(dow, parity, today, settings);
            var rows = _app.Db.GetLessons(settings.MyGroupId, dow, weekCode)
                .OrderBy(l => TimeSpan.TryParse(l.TimeStart, out var t) ? t : TimeSpan.Zero)
                .Select(l => new WeekRow(
                    l.TimeStart,
                    ScheduleComposer.StripType(_app.Overrides.GetDisplayName(l.SubjectRaw, l.DayOfWeek), l.TypeRaw),
                    DayTitles.TypeLabel(l.TypeRaw, loc),
                    RoomLabel(l, loc)))
                .ToList();
            total += rows.Count;
            days.Add(new WeekDay(dow, loc.T(DayKeys[dow - 1]), date, date == today.Date, rows));
        }
        return new WeekModel(parity, isOddToday, true, total, days);
    }

    /// <summary>The first date ≥ today on this weekday whose user-facing parity matches (a two-week cycle always hits within 14 days).</summary>
    public static DateTime NearestDate(int dow, int parity, DateTime today, Settings settings)
    {
        var (periodStart, weekCount) = Period(settings, today);
        for (var i = 0; i < 14; i++)
        {
            var d = today.Date.AddDays(i);
            if ((int)d.DayOfWeek != dow) continue;
            if (ParityService.IsOddWeek(d, periodStart, weekCount, settings.ParityInvert) == (parity == 1)) return d;
        }
        return today.Date;
    }

    private string RoomLabel(Lesson l, Loc loc)
    {
        var (room, tag, _) = ScheduleComposer.RoomParts(l, _app.Maps.Resolve(l.ClassroomRaw), loc);
        return tag is null ? room : $"{room} {tag}";
    }

    private static (DateTime PeriodStart, int WeekCount) Period(Settings s, DateTime today) =>
        (DateTime.TryParse(s.PeriodStart, out var ps) ? ps : new DateTime(today.Year, 9, 1), s.WeekCount > 0 ? s.WeekCount : 2);
}
