using Vograph.Core.Models;

namespace Vograph.Desktop.Features.Schedule;

public static class SmartStart
{
    /// <summary>Open "today" while at least one lesson is still running or ahead; otherwise "tomorrow".</summary>
    public static int InitialOffset(IEnumerable<Lesson> todayLessons, TimeSpan now)
    {
        foreach (var l in todayLessons)
            if (TimeSpan.TryParse(l.TimeEnd, out var end) && end > now) return 0;
        return 1;
    }
}
