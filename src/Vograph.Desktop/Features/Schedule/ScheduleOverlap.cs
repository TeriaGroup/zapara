namespace Vograph.Desktop.Features.Schedule;

public sealed record ScheduleOverlap(LessonRowViewModel First, LessonRowViewModel Second)
{
    public string Caption => $"{First.DisplayName} {First.TimeStart}–{First.TimeEnd} и {Second.DisplayName} {Second.TimeStart}–{Second.TimeEnd}";

    public static IReadOnlyList<ScheduleOverlap> Find(IReadOnlyList<LessonRowViewModel> rows)
    {
        var result = new List<ScheduleOverlap>();
        for (var first = 0; first < rows.Count; first++)
        for (var second = first + 1; second < rows.Count; second++)
        {
            if (!TimeSpan.TryParse(rows[first].TimeStart, out var a0) || !TimeSpan.TryParse(rows[first].TimeEnd, out var a1)
                || !TimeSpan.TryParse(rows[second].TimeStart, out var b0) || !TimeSpan.TryParse(rows[second].TimeEnd, out var b1)) continue;
            if (a0 < b1 && b0 < a1) result.Add(new(rows[first], rows[second]));
        }
        return result;
    }
}
