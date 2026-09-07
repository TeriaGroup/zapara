using Vograph.Core.Models;
using Vograph.Core.Services;

namespace Vograph.Desktop.Domain;

/// <summary>
/// The one place that knows how the parity a user sees relates to the XML week codes stored in schedule_cache,
/// and what Core's fallbacks for the period are. Replaces the same five lines that used to be repeated in every
/// composer (period fallback ×5, inversion ×3).
/// </summary>
public static class ParityCodes
{
    /// <summary>Settings.PeriodStart / WeekCount with Core's own fallbacks: 1 September of the anchor's year, two weeks.</summary>
    public static (DateTime PeriodStart, int WeekCount) Period(Settings s, DateTime anchor) =>
        (DateTime.TryParse(s.PeriodStart, out var ps) ? ps : new DateTime(anchor.Year, 9, 1), s.WeekCount > 0 ? s.WeekCount : 2);

    /// <summary>The XML week code (1 odd / 2 even) the cache stores for the lessons of that date, inversion applied.
    /// Loops must resolve the period ONCE (from their start date) and use the overload below — otherwise a missing
    /// PeriodStart would be re-anchored on every iterated day and change year across New Year.</summary>
    public static int WeekCode(DateTime date, Settings s) => WeekCode(date, s, Period(s, date));

    public static int WeekCode(DateTime date, Settings s, (DateTime PeriodStart, int WeekCount) period) =>
        ToXml(ParityService.GetWeekCode(date, period.PeriodStart, period.WeekCount), s.ParityInvert);

    /// <summary>Whether the user sees that date as an odd week (inversion applied).</summary>
    public static bool IsOdd(DateTime date, Settings s) => IsOdd(date, s, Period(s, date));

    public static bool IsOdd(DateTime date, Settings s, (DateTime PeriodStart, int WeekCount) period) =>
        ParityService.IsOddWeek(date, period.PeriodStart, period.WeekCount, s.ParityInvert);

    /// <summary>User-facing parity → XML code. Identical unless the user inverted parity; an involution, so ToUser is the same map.</summary>
    public static int ToXml(int parity, bool invert) => invert ? (parity == 1 ? 2 : 1) : parity;

    public static int ToUser(int xmlParity, bool invert) => ToXml(xmlParity, invert);
}
