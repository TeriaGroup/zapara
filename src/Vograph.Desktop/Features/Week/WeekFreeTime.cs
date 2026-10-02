using Zapara.Client.Domain;

namespace Vograph.Desktop.Features.Week;

public static class WeekFreeTime
{
    public static IReadOnlyList<string> Between(IEnumerable<WeekRow> rows)
    {
        var intervals = rows.Select(row => TimeSpan.TryParse(row.Time, out var start) &&
            TimeSpan.TryParse(row.TimeEnd, out var end) ? new DayInterval(start, end) : null)
            .OfType<DayInterval>();
        return DayPlanning.FreeTime(intervals).Select(gap =>
        {
            var hours = gap.Minutes / 60;
            var minutes = gap.Minutes % 60;
            var duration = hours > 0 ? $"{hours} ч" + (minutes > 0 ? $" {minutes} мин" : "") : $"{minutes} мин";
            return $"{gap.Start.ToString("hh\\:mm")}–{gap.End.ToString("hh\\:mm")} · {duration}";
        }).ToArray();
    }
}
