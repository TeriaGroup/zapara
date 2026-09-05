using System.Globalization;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Shell;

public static class GroupCardLogic
{
    /// <summary>Chip text when the timetable is older than 3 days; Warn when older than 7.</summary>
    public static (string? Text, bool Warn) Stale(string? lastFetchedAt, DateTime utcNow, Loc loc)
    {
        if (!DateTime.TryParse(lastFetchedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last)) return (null, false);
        var age = utcNow - last.ToUniversalTime();
        if (age.TotalDays <= 3) return (null, false);
        return (loc.T("updatedChip", DayTitles.ShortDate(last.ToLocalTime(), loc)), age.TotalDays > 7);
    }
}
