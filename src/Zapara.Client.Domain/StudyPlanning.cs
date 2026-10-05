using System.Globalization;

namespace Zapara.Client.Domain;

public sealed record StudyInterval(string Start, string End);
public sealed record FreeStudyInterval(string Start, string End, int Minutes);
public sealed record TransferAssessment(string Status, int? AvailableSeconds, int? RouteSeconds);

public static class StudyPlanning
{
    private static int? Minutes(string value) => TimeOnly.TryParseExact(value, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var time) ? time.Hour * 60 + time.Minute : null;
    private static string Time(int value) => (value / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (value % 60).ToString("00", CultureInfo.InvariantCulture);

    public static IReadOnlyList<FreeStudyInterval> CommonFreeIntervals(IEnumerable<StudyInterval> first,
        IEnumerable<StudyInterval> second, int minimumMinutes = 15)
    {
        var a = first.Select(row => (Start: Minutes(row.Start), End: Minutes(row.End))).ToArray();
        var b = second.Select(row => (Start: Minutes(row.Start), End: Minutes(row.End))).ToArray();
        if (a.Length == 0 || b.Length == 0 || minimumMinutes < 1 ||
            a.Concat(b).Any(row => row.Start is null || row.End is null || row.End <= row.Start)) return [];
        var low = Math.Max(a.Min(row => row.Start!.Value), b.Min(row => row.Start!.Value));
        var high = Math.Min(a.Max(row => row.End!.Value), b.Max(row => row.End!.Value));
        if (high <= low) return [];
        var result = new List<FreeStudyInterval>(); var cursor = low;
        foreach (var row in a.Concat(b).OrderBy(row => row.Start).ThenBy(row => row.End))
        {
            if (row.End <= low || row.Start >= high) continue;
            var start = Math.Max(low, row.Start!.Value); var end = Math.Min(high, row.End!.Value);
            if (start - cursor >= minimumMinutes) result.Add(new(Time(cursor), Time(start), start - cursor));
            cursor = Math.Max(cursor, end);
        }
        if (high - cursor >= minimumMinutes) result.Add(new(Time(cursor), Time(high), high - cursor));
        return result;
    }

    public static TransferAssessment AssessTransfer(string previousEnd, string nextStart, int? routeSeconds)
    {
        var end = Minutes(previousEnd); var start = Minutes(nextStart);
        var route = routeSeconds is >= 0 ? routeSeconds : null;
        if (end is null || start is null) return new("unknown", null, route);
        var available = (start.Value - end.Value) * 60;
        return new(available < 0 ? "overlap" : route is null ? "unknown" : route > available ? "tight" : "fits", available, route);
    }
}
