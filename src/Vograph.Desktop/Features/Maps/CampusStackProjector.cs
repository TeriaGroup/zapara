using Vograph.Core.Campus;

namespace Vograph.Desktop.Features.Maps;

public readonly record struct StackPoint3(double X, double Y, double Z);

public readonly record struct StackPoint2(double X, double Y);

public sealed record StackPolyline(
    string Kind,
    int Floor,
    int? ToFloor,
    IReadOnlyList<StackPoint3> Points,
    IReadOnlyList<StackPoint2> Points2);

public sealed record StackQuad(
    int Floor,
    IReadOnlyList<StackPoint3> Points,
    IReadOnlyList<StackPoint2> Points2);

public sealed record StackScene(
    IReadOnlyList<StackQuad> Floors,
    IReadOnlyList<StackPolyline> Polylines);

public readonly record struct StackPaintItem(
    string Kind,
    int Floor,
    StackQuad? Quad,
    StackPolyline? Line);

/// <summary>Orthographic stack: floor quads + walk/stair polylines. No GPU.</summary>
public static class CampusStackProjector
{
    public const double DefaultYaw = Math.PI / 4;
    public const double DefaultPitch = Math.PI / 6;
    public const double FloorGap = 0.55;

    /// <summary>Walks from <see cref="MapsComposer.FloorPathStrokes"/>; stairs are z-segments at shaft xy.</summary>
    public static IReadOnlyList<StackPolyline> RoutePolylines(Route? route, string building)
    {
        if (route is null) return [];
        List<StackPolyline> lines = [];
        SortedSet<int> floors = [];
        foreach (var leg in route.Legs)
        {
            if (leg.Kind is "walk" or "building_link")
                floors.Add(leg.Floor);
        }
        foreach (var floor in floors)
        {
            foreach (var stroke in MapsComposer.FloorPathStrokes(route, building, floor))
            {
                if (stroke.Count < 2) continue;
                var pts = new StackPoint3[stroke.Count];
                for (var i = 0; i < stroke.Count; i++)
                    pts[i] = new StackPoint3(stroke[i].x, stroke[i].y, floor);
                lines.Add(new StackPolyline("walk", floor, null, pts, []));
            }
        }
        foreach (var leg in route.Legs)
        {
            if (leg.Kind is not ("stair_up" or "stair_down")) continue;
            if (!string.Equals(leg.Building, building, StringComparison.Ordinal)) continue;
            if (leg.Points.Count == 0) continue;
            var from = leg.Points[0];
            var to = leg.Points[leg.Points.Count - 1];
            var z0 = leg.Floor;
            var z1 = leg.ToFloor ?? leg.Floor;
            lines.Add(new StackPolyline(leg.Kind, z0, z1, [
                new StackPoint3(from.X, from.Y, z0),
                new StackPoint3(to.X, to.Y, z1)
            ], []));
        }
        return lines;
    }

    public static StackScene Project(
        Route? route,
        string building,
        double width,
        double height,
        double yaw = DefaultYaw,
        double pitch = DefaultPitch) =>
        Project(route, building, MapsComposer.Floors(building), width, height, yaw, pitch);

    public static StackScene Project(
        Route? route,
        string building,
        IReadOnlyList<int> floors,
        double width,
        double height,
        double yaw = DefaultYaw,
        double pitch = DefaultPitch)
    {
        var cam = new Camera(width, height, yaw, pitch, ZMid(floors));
        var quads = new StackQuad[floors.Count];
        for (var i = 0; i < floors.Count; i++)
        {
            var z = floors[i];
            StackPoint3[] pts3 = [new(0, 0, z), new(1, 0, z), new(1, 1, z), new(0, 1, z)];
            quads[i] = new StackQuad(z, pts3, Map(pts3, cam));
        }
        var raw = RoutePolylines(route, building);
        var projected = new StackPolyline[raw.Count];
        for (var i = 0; i < raw.Count; i++)
        {
            var line = raw[i];
            projected[i] = line with { Points2 = Map(line.Points, cam) };
        }
        return new StackScene(quads, projected);
    }

    /// <summary>Back-to-front: each floor raster, then walks on that floor, then stairs leaving it.</summary>
    public static IReadOnlyList<StackPaintItem> PaintSequence(StackScene scene)
    {
        List<StackPaintItem> items = [];
        foreach (var quad in scene.Floors)
        {
            items.Add(new("floor", quad.Floor, quad, null));
            foreach (var line in scene.Polylines)
            {
                if (line.Kind == "walk" && line.Floor == quad.Floor)
                    items.Add(new("walk", line.Floor, null, line));
            }
            foreach (var line in scene.Polylines)
            {
                if (line.Kind is "stair_up" or "stair_down" && line.Floor == quad.Floor)
                    items.Add(new(line.Kind, line.Floor, null, line));
            }
        }
        return items;
    }

    public static StackPoint2 ProjectPoint(
        StackPoint3 p,
        double width,
        double height,
        IReadOnlyList<int> floors,
        double yaw = DefaultYaw,
        double pitch = DefaultPitch) =>
        MapPoint(p, new Camera(width, height, yaw, pitch, ZMid(floors)));

    private static IReadOnlyList<StackPoint2> Map(IReadOnlyList<StackPoint3> pts, Camera cam)
    {
        var mapped = new StackPoint2[pts.Count];
        for (var i = 0; i < pts.Count; i++)
            mapped[i] = MapPoint(pts[i], cam);
        return mapped;
    }

    private static StackPoint2 MapPoint(StackPoint3 p, Camera cam)
    {
        var cx = p.X - 0.5;
        var cy = p.Y - 0.5;
        var cz = (p.Z - cam.ZMid) * FloorGap;
        var cosY = Math.Cos(cam.Yaw);
        var sinY = Math.Sin(cam.Yaw);
        var rx = cx * cosY - cy * sinY;
        var ry = cx * sinY + cy * cosY;
        var sx = rx;
        var sy = ry * Math.Cos(cam.Pitch) - cz * Math.Sin(cam.Pitch);
        var scale = Math.Min(cam.Width, cam.Height) * 0.72;
        return new StackPoint2(cam.Width * 0.5 + sx * scale, cam.Height * 0.5 + sy * scale);
    }

    private static double ZMid(IReadOnlyList<int> floors)
    {
        if (floors.Count == 0) return 1;
        var min = floors[0];
        var max = floors[0];
        foreach (var f in floors)
        {
            if (f < min) min = f;
            if (f > max) max = f;
        }
        return (min + max) / 2.0;
    }

    private readonly record struct Camera(double Width, double Height, double Yaw, double Pitch, double ZMid);
}
