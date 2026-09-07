using Vograph.Core.Models;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Week;

public sealed record WeekRow(string Time, string Name, string TypeLabel, string Room);
public sealed record WeekDay(int Dow, string Title, DateTime Date, bool IsToday, IReadOnlyList<WeekRow> Rows);
/// <param name="Parity">1 odd / 2 even as the user sees it (inversion already applied).</param>
public sealed record WeekModel(int Parity, bool IsOddToday, bool HasGroup, int Total, IReadOnlyList<WeekDay> Days);

/// <summary>Six day cards of one parity. Synchronous and DB-bound — call from ViewModelBase.RunAsync.</summary>
public sealed class WeekComposer
{
    private readonly AppServices _app;

    public WeekComposer(AppServices app) => _app = app;

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
                    RoomLabel(l, loc)))
                .ToList();
            total += rows.Count;
            days.Add(new WeekDay(dow, loc.T(DayNames.Key(dow)), date, date == today.Date, rows));
        }
        return new WeekModel(parity, isOddToday, true, total, days);
    }

    /// <summary>The first date ≥ today on this weekday whose user-facing parity matches (a two-week cycle always hits within 14 days).</summary>
    public static DateTime NearestDate(int dow, int parity, DateTime today, Settings settings)
    {
        for (var i = 0; i < 14; i++)
        {
            var d = today.Date.AddDays(i);
            if ((int)d.DayOfWeek != dow) continue;
            if (ParityCodes.IsOdd(d, settings) == (parity == 1)) return d;
        }
        return today.Date;
    }

    private string RoomLabel(Lesson l, Loc loc)
    {
        var (room, tag, _) = LessonText.RoomParts(l, _app.Maps.Resolve(l.ClassroomRaw), loc);
        return tag is null ? room : $"{room} {tag}";
    }
}
