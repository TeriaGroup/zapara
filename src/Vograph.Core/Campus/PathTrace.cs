namespace Vograph.Core.Campus;

public readonly record struct TracePoint(double X, double Y);

public sealed record PathTraceFrame(
    IReadOnlyList<IReadOnlyList<TracePoint>> Revealed,
    TracePoint? Head,
    TracePoint? Start,
    TracePoint? End,
    bool Complete);

/// <summary>Polyline draw-on: prefix, traveler, and dash phase. Progress is linear; the UI applies easing.</summary>
public static class PathTrace
{
    public const int DrawMs = 1200;
    public const int LoopMs = 1200;
    public const double DashPeriod = 18;

    public static double Length(IReadOnlyList<TracePoint> points)
    {
        if (points is null || points.Count < 2) return 0;
        double sum = 0;
        for (var i = 1; i < points.Count; i++)
            sum += Dist(points[i - 1], points[i]);
        return sum;
    }

    public static double Length(IReadOnlyList<IReadOnlyList<TracePoint>> strokes)
    {
        if (strokes is null || strokes.Count == 0) return 0;
        double sum = 0;
        foreach (var stroke in strokes) sum += Length(stroke);
        return sum;
    }

    public static IReadOnlyList<TracePoint> Prefix(IReadOnlyList<TracePoint> points, double distance)
    {
        if (points is null || points.Count == 0) return [];
        if (distance <= 0) return [points[0]];
        List<TracePoint> result = [points[0]];
        var remaining = distance;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var len = Dist(a, b);
            if (len < 1e-12)
            {
                result.Add(b);
                continue;
            }
            if (remaining >= len - 1e-12)
            {
                result.Add(b);
                remaining -= len;
                if (remaining <= 1e-12) return result;
                continue;
            }
            var t = remaining / len;
            result.Add(new TracePoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
            return result;
        }
        return result;
    }

    public static TracePoint? PointAt(IReadOnlyList<TracePoint> points, double distance)
    {
        if (points is null || points.Count == 0) return null;
        if (points.Count == 1) return points[0];
        if (distance <= 0) return points[0];
        var remaining = distance;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var len = Dist(a, b);
            if (len < 1e-12) continue;
            if (remaining >= len - 1e-12)
            {
                remaining -= len;
                continue;
            }
            var t = remaining / len;
            return new TracePoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }
        return points[^1];
    }

    public static PathTraceFrame At(IReadOnlyList<IReadOnlyList<TracePoint>> strokes, double progress)
    {
        if (strokes is null || strokes.Count == 0)
            return new PathTraceFrame([], null, null, null, true);

        TracePoint? start = null;
        TracePoint? end = null;
        foreach (var stroke in strokes)
        {
            if (stroke is null || stroke.Count == 0) continue;
            start ??= stroke[0];
            end = stroke[^1];
        }
        var total = Length(strokes);
        if (total <= 1e-12 || start is null)
            return new PathTraceFrame([], start, start, end, true);

        var t = progress < 0 ? 0 : progress > 1 ? 1 : progress;
        var target = t * total;
        List<IReadOnlyList<TracePoint>> revealed = [];
        TracePoint? head = start;
        var remaining = target;
        foreach (var stroke in strokes)
        {
            var len = Length(stroke);
            if (stroke is null || stroke.Count == 0) continue;
            if (len <= 1e-12) continue;
            if (remaining >= len - 1e-12)
            {
                revealed.Add(stroke);
                head = stroke[^1];
                remaining -= len;
                continue;
            }
            if (remaining > 1e-12)
            {
                var prefix = Prefix(stroke, remaining);
                if (prefix.Count >= 2) revealed.Add(prefix);
                head = prefix.Count == 0 ? stroke[0] : prefix[^1];
            }
            remaining = 0;
            break;
        }
        return new PathTraceFrame(revealed, head, start, end, t >= 1 - 1e-9);
    }

    public static double DrawProgress(double elapsedMs, int durationMs)
    {
        if (durationMs <= 0) return 1;
        if (elapsedMs <= 0) return 0;
        var t = elapsedMs / durationMs;
        return t >= 1 ? 1 : t;
    }

    public static double LoopProgress(double elapsedMs, int durationMs, int loopMs)
    {
        if (durationMs <= 0 || loopMs <= 0) return 1;
        if (elapsedMs < durationMs) return 1;
        var along = (elapsedMs - durationMs) % loopMs;
        if (along < 0) along += loopMs;
        return along / loopMs;
    }

    public static double DashOffset(double elapsedMs, int cycleMs, double period)
    {
        if (cycleMs <= 0 || period <= 0) return 0;
        var along = elapsedMs % cycleMs;
        if (along < 0) along += cycleMs;
        return along / cycleMs * period;
    }

    private static double Dist(TracePoint a, TracePoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
