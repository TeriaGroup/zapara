using Vograph.Core.Models;

namespace Vograph.Desktop.Domain;

public static class SmartStart
{
    /// <summary>Open "today" while at least one lesson is still running or ahead; otherwise "tomorrow".</summary>
    public static int InitialOffset(IEnumerable<Lesson> todayLessons, TimeSpan now) =>
        InitialOffset(todayLessons, now, DayOfWeek.Monday);

    public static int InitialOffset(IEnumerable<Lesson> todayLessons, TimeSpan now, DayOfWeek dayOfWeek)
    {
        if (dayOfWeek == DayOfWeek.Sunday) return 1;
        foreach (var l in todayLessons)
            if (TimeSpan.TryParse(l.TimeEnd, out var end) && end > now) return 0;
        return dayOfWeek == DayOfWeek.Saturday ? 2 : 1;
    }
}
