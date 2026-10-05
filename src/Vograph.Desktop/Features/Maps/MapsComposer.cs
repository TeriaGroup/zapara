using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Vograph.Core.Campus;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Maps;

public enum MapMode { None, NextLesson, Lesson, Manual }

public sealed record StairMarker(double X, double Y, string Label, bool IsDeparture);

public static class MapsComposer
{
    public static IReadOnlyList<StairMarker> StairMarkers(Route? route, string building, int floor)
    {
        if (route is null) return [];
        var markers = new List<StairMarker>();
        foreach (var leg in route.Legs)
        {
            if (leg.Kind is not ("stair_up" or "stair_down") || leg.Points.Count == 0) continue;
            var arrow = leg.Kind == "stair_up" ? "↑" : "↓";
            if (leg.Building == building && leg.Floor == floor && leg.ToFloor is { } next)
                Add(leg.Points[0], $"{arrow} {next}", true);
            if ((leg.ToBuilding ?? leg.Building) == building && leg.ToFloor == floor)
                Add(leg.Points[^1], $"{arrow} с {leg.Floor}", false);
        }
        return markers;

        void Add(GraphPoint point, string label, bool departure)
        {
            var marker = new StairMarker(point.X, point.Y, label, departure);
            var existing = markers.FindIndex(m => Math.Abs(m.X - point.X) < 0.00001 && Math.Abs(m.Y - point.Y) < 0.00001);
            if (existing < 0) markers.Add(marker);
            else if (departure && !markers[existing].IsDeparture) markers[existing] = marker;
        }
    }

    public static IReadOnlyList<int> Floors(string building) => building == "УЛК" ? new[] { 1, 2, 3, 4, 5 } : new[] { 1, 2, 3, 4 };

    public static Node? PreferredEntrance(CampusGraph graph, string building)
    {
        var shown = building == "ВЦ" ? "ГК" : building;
        Node? first = null;
        foreach (var node in graph.Nodes)
        {
            if (node.Kind != "entrance" || node.Building != shown) continue;
            first ??= node;
            if (node.Id.EndsWith(".main", StringComparison.Ordinal)) return node;
        }
        return first;
    }

    public static Node? StartFor(CampusGraph graph, string? fromId, string? toId)
    {
        Node? dest = null;
        if (!string.IsNullOrWhiteSpace(toId))
        {
            foreach (var node in graph.Nodes)
            {
                if (node.Id == toId) { dest = node; break; }
            }
        }
        Node? requested = null;
        if (!string.IsNullOrWhiteSpace(fromId))
        {
            foreach (var node in graph.Nodes)
            {
                if (node.Id == fromId) { requested = node; break; }
            }
        }
        if (dest is null || requested is null) return requested;
        var candidates = new List<string> { requested.Id };
        if (PreferredEntrance(graph, dest.Building) is { } preferred) candidates.Add(preferred.Id);
        foreach (var id in candidates.Distinct(StringComparer.Ordinal))
        {
            if (CampusRouter.Find(graph, id, dest.Id).Ok)
            {
                foreach (var node in graph.Nodes)
                    if (node.Id == id) return node;
            }
        }
        return requested;
    }

    public static string? StartFallbackMessage(Node? requested, Node? chosen, I18nService i18n)
    {
        if (requested is null || chosen is null || requested.Id == chosen.Id) return null;
        var label = chosen.Kind == "entrance"
            ? (string.IsNullOrWhiteSpace(chosen.Label) ? chosen.Id : chosen.Label!)
            : chosen.Room ?? chosen.Label ?? chosen.Id;
        return i18n.T("mapsRouteStartFallback", label);
    }

    public const int StackThumbWidth = 256;

    /// <summary>Local rasters for each floor of the shown building. Missing files are omitted (schematic fill).</summary>
    public static IReadOnlyDictionary<int, string> FloorRasterPaths(
        string building,
        IEnumerable<MapInfo> maps,
        Func<MapInfo, string?> localPath)
    {
        var shown = building == "ВЦ" ? "ГК" : building;
        Dictionary<int, string> result = [];
        foreach (var floor in Floors(shown))
        {
            var map = maps.FirstOrDefault(m => (m.Building == "ВЦ" ? "ГК" : m.Building) == shown && m.Floor == floor);
            if (map is null) continue;
            string? path;
            try { path = localPath(map); }
            catch { continue; }
            if (!string.IsNullOrEmpty(path)) result[floor] = path;
        }
        return result;
    }

    public static Bitmap? DecodeStackThumb(string path)
    {
        using var stream = File.OpenRead(path);
        return Bitmap.DecodeToWidth(stream, StackThumbWidth);
    }

    /// <summary>«через 25 мин» / «через 18 ч» / «через 2 дн.» / «идёт сейчас».</summary>
    public static string Until(DateTime now, DateTime start, DateTime end, Loc loc)
    {
        if (now >= end) return "";
        if (now >= start) return loc.T("mapNow");
        var span = start - now;
        if (span.TotalMinutes < 60) return loc.T("mapInMinutes", Math.Max(1, (int)Math.Round(span.TotalMinutes)));
        if (span.TotalHours < 48) return loc.T("mapInHours", (int)Math.Round(span.TotalHours));
        return loc.T("mapInDays", (int)Math.Round(span.TotalDays));
    }

    public static string ContextLine(MapMode mode, MapInfo? map, string? lessonName, DateTime? start, DateTime? end, DateTime now, Loc loc)
    {
        switch (mode)
        {
            case MapMode.None: return loc.T("mapNoNext");
            case MapMode.Manual: return map is null ? loc.T("mapPickPlan") : Place(map, loc);
        }
        var where = map is null ? "" : $" · {RoomText(map)} · {Place(map, loc)}";
        if (mode == MapMode.Lesson) return loc.T("mapLessonPrefix", lessonName ?? RoomText(map!)) + where;
        var when = start is { } s && end is { } e && now < e ? $" · {Until(now, s, e, loc)}" : "";
        return loc.T("mapNextLesson") + where + when;
    }

    /// <summary>Relative coords.json rectangle → image pixels; null without coordinates (only the header is shown then).</summary>
    public static Rect? Highlight(CoordsRect? coords, PixelSize image)
    {
        if (coords is null || image.Width <= 0 || image.Height <= 0) return null;
        return new Rect(coords.x * image.Width, coords.y * image.Height, coords.w * image.Width, coords.h * image.Height);
    }

    /// <summary>Normalized 0–1 route points → image pixels; same multiply as Highlight. Empty without points or a sized image.</summary>
    public static IEnumerable<Point> PathPixels(IReadOnlyList<(double x, double y)>? points, PixelSize image)
    {
        if (points is null || points.Count == 0 || image.Width <= 0 || image.Height <= 0) return [];
        var pixels = new Point[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            pixels[i] = new Point(p.x * image.Width, p.y * image.Height);
        }
        return pixels;
    }

    /// <summary>Walk and same-plan link points; transfers between separate plans have no shared 2D coordinates.</summary>
    public static IReadOnlyList<(double x, double y)> FloorPathPoints(Route? route, string building, int floor)
    {
        var strokes = FloorPathStrokes(route, building, floor);
        if (strokes.Count == 0) return [];
        var points = new List<(double x, double y)>();
        foreach (var stroke in strokes)
            points.AddRange(stroke);
        return points;
    }

    /// <summary>One stroke per walk/link leg so disconnected wings on the same floor stay separate polylines.</summary>
    public static IReadOnlyList<IReadOnlyList<(double x, double y)>> FloorPathStrokes(Route? route, string building, int floor)
    {
        if (route is null) return [];
        List<IReadOnlyList<(double x, double y)>> strokes = [];
        foreach (var leg in route.Legs)
        {
            if (!DrawnOnFloor(leg, building, floor) || leg.Points.Count == 0) continue;
            var pts = new (double x, double y)[leg.Points.Count];
            for (var i = 0; i < leg.Points.Count; i++)
                pts[i] = (leg.Points[i].X, leg.Points[i].Y);
            strokes.Add(pts);
        }
        return strokes;
    }

    private static bool DrawnOnFloor(Leg leg, string building, int floor) =>
        leg.Floor == floor
        && string.Equals(leg.Building, building, StringComparison.Ordinal)
        && (leg.Kind == "walk"
            || (leg.Kind == "building_link" && leg.ToBuilding == building && leg.ToFloor == floor));

    /// <summary>Disconnected walk/link strokes as open figures so a single Path does not join separate wings.</summary>
    public static PathGeometry? PathGeometry(IReadOnlyList<IReadOnlyList<Point>>? strokes)
    {
        if (strokes is null || strokes.Count == 0) return null;
        var figures = new PathFigures();
        foreach (var stroke in strokes)
        {
            if (stroke.Count < 2) continue;
            figures.Add(new PathFigure
            {
                StartPoint = stroke[0],
                IsFilled = false,
                IsClosed = false,
                Segments = [new PolyLineSegment(stroke.Skip(1))],
            });
        }
        return figures.Count == 0 ? null : new PathGeometry { Figures = figures };
    }

    /// <summary>Where the room label sits in viewport space: above the top-left corner of the highlight, kept inside
    /// the viewport at both ends — a plan panned right or down must not push the chip out of the clipped card.</summary>
    public static Point LabelOffset(double scale, double offsetX, double offsetY, double left, double top, Size viewport, Size label) =>
        new(Inside(offsetX + left * scale, viewport.Width - label.Width),
            Inside(offsetY + top * scale - 26, viewport.Height - label.Height));

    private static double Inside(double value, double max) => Math.Clamp(value, 0, Math.Max(0, max));

    public static string RoomText(MapInfo map) => string.IsNullOrWhiteSpace(map.ClassroomRaw) ? map.RoomRaw : LessonText.CleanRoom(map.ClassroomRaw);

    /// <summary>Last lesson today that already started and is before the target; null when the target is not today (use the last entrance).</summary>
    public static Lesson? PreviousLessonToday(IEnumerable<Lesson> today, Lesson target, DateTime now, DateTime targetDate)
    {
        if (targetDate.Date != now.Date) return null;
        if (!TimeSpan.TryParse(target.TimeStart, out var targetStart)) return null;
        var nowTime = now.TimeOfDay;
        Lesson? prev = null;
        foreach (var lesson in today.OrderBy(l => TimeSpan.TryParse(l.TimeStart, out var t) ? t : TimeSpan.MaxValue))
        {
            if (!TimeSpan.TryParse(lesson.TimeStart, out var start)) continue;
            if (start < targetStart && start <= nowTime)
                prev = lesson;
        }
        return prev;
    }

    /// <summary>«ГК, 4 этаж» — the plan actually shown (ВЦ lessons show the ГК plan).</summary>
    public static string Place(MapInfo map, Loc loc) => $"{(map.Building == "ВЦ" ? "ГК" : map.Building)}, {loc.T("mapFloorN", map.Floor)}";
}
