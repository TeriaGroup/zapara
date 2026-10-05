namespace Zapara.Client.Domain;

public sealed record DayInterval(TimeSpan Start, TimeSpan End);
public sealed record FreeTimeInterval(TimeSpan Start, TimeSpan End)
{
    public int Minutes => (int)(End - Start).TotalMinutes;
}

/// <summary>Calendar and interval rules used by day planning, without UI or storage dependencies.</summary>
public static class DayPlanning
{
    public static IReadOnlyList<DateOnly> VisibleDates(DateOnly today, DateOnly selected, int count)
    {
        if (count is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(count));
        var inCurrentStrip = selected.DayNumber >= today.DayNumber && selected.DayNumber < today.DayNumber + count;
        var start = inCurrentStrip ? today.DayNumber : selected.DayNumber - count / 2;
        start = Math.Clamp(start, DateOnly.MinValue.DayNumber, DateOnly.MaxValue.DayNumber - count + 1);
        return Enumerable.Range(0, count).Select(offset => DateOnly.FromDayNumber(start + offset)).ToArray();
    }

    public static IReadOnlyList<FreeTimeInterval> FreeTime(IEnumerable<DayInterval> intervals, int minimumMinutes = 1)
    {
        if (minimumMinutes < 1) throw new ArgumentOutOfRangeException(nameof(minimumMinutes));
        var sorted = intervals.Where(x => x.Start >= TimeSpan.Zero && x.End <= TimeSpan.FromDays(1) && x.End > x.Start)
            .OrderBy(x => x.Start).ThenBy(x => x.End).ToArray();
        if (sorted.Length < 2) return [];
        var gaps = new List<FreeTimeInterval>();
        var mergedEnd = sorted[0].End;
        foreach (var interval in sorted.Skip(1))
        {
            if ((interval.Start - mergedEnd).TotalMinutes >= minimumMinutes) gaps.Add(new(mergedEnd, interval.Start));
            if (interval.End > mergedEnd) mergedEnd = interval.End;
        }
        return gaps;
    }

    public static bool InDeadlineWindow(DateOnly selectedDay, DateOnly? deadline, bool subjectIsVisible)
        => deadline is { } day
            ? day.DayNumber >= selectedDay.DayNumber && day.DayNumber <= selectedDay.DayNumber + 2
            : subjectIsVisible;

    public static int PriorityIndex(IReadOnlyList<DayInterval> lessons, DateOnly selectedDay, DateOnly today, TimeSpan now)
    {
        if (selectedDay < today || lessons.Count == 0) return -1;
        var candidates = Enumerable.Range(0, lessons.Count).Where(i => lessons[i].End > lessons[i].Start);
        if (selectedDay == today)
        {
            var current = candidates.Where(i => lessons[i].Start <= now && lessons[i].End > now).OrderBy(i => lessons[i].Start).ToArray();
            if (current.Length > 0) return current[0];
            candidates = candidates.Where(i => lessons[i].Start > now);
        }
        return candidates.OrderBy(i => lessons[i].Start).DefaultIfEmpty(-1).First();
    }
}
