namespace Vograph.Core.Campus;

internal static class CampusBlocked
{
    internal static bool WalkBlocked(Edge edge, Node from, Node to, IReadOnlyList<BlockedRegion>? blocked)
    {
        if (edge.Kind != "walk" || blocked is not { Count: > 0 }) return false;
        IReadOnlyList<GraphPoint> pts = edge.Points is { Count: > 0 } poly
            ? poly
            : [new GraphPoint(from.X, from.Y), new GraphPoint(to.X, to.Y)];
        foreach (var region in blocked)
        {
            if (region.Building != from.Building || region.Floor != from.Floor) continue;
            if (region.OwnerId is { } owner && (owner == from.Id || owner == to.Id)) continue;
            if (Crosses(pts, region.Left, region.Top, region.Right, region.Bottom))
                return true;
        }
        return false;
    }

    internal static bool Crosses(
        IReadOnlyList<GraphPoint> points, double left, double top, double right, double bottom)
    {
        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            for (var s = 1; s < 100; s++)
            {
                var x = a.X + (b.X - a.X) * s / 100;
                var y = a.Y + (b.Y - a.Y) * s / 100;
                if (x > left && x < right && y > top && y < bottom)
                    return true;
            }
        }
        return false;
    }
}
