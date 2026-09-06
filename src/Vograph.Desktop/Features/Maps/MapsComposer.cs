using Avalonia;
using Vograph.Core.Services;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Maps;

public enum MapMode { None, NextLesson, Lesson, Manual }

public static class MapsComposer
{
    public static IReadOnlyList<int> Floors(string building) => building == "УЛК" ? new[] { 1, 2, 3, 4, 5 } : new[] { 1, 2, 3, 4 };

    /// <summary>«через 25 мин» / «через 18 ч» / «через 2 дн.» / «идёт сейчас».</summary>
    public static string Until(DateTime now, DateTime start, DateTime end, Loc loc)
    {
        if (now >= start && now < end) return loc.T("mapNow");
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
            case MapMode.Manual: return loc.T("mapPickPlan");
        }
        var where = map is null ? "" : $" · {RoomText(map)} · {Place(map, loc)}";
        if (mode == MapMode.Lesson) return loc.T("mapLessonPrefix", lessonName ?? RoomText(map!)) + where;
        var when = start is { } s && end is { } e ? $" · {Until(now, s, e, loc)}" : "";
        return loc.T("mapNextLesson") + where + when;
    }

    /// <summary>Relative coords.json rectangle → image pixels; null without coordinates (only the header is shown then).</summary>
    public static Rect? Highlight(CoordsRect? coords, PixelSize image)
    {
        if (coords is null || image.Width <= 0 || image.Height <= 0) return null;
        return new Rect(coords.x * image.Width, coords.y * image.Height, coords.w * image.Width, coords.h * image.Height);
    }

    public static string RoomText(MapInfo map) => string.IsNullOrWhiteSpace(map.ClassroomRaw) ? map.RoomRaw : map.ClassroomRaw.Trim().TrimEnd(';').Replace("*", "").Trim();

    /// <summary>«ГК, 4 этаж» — the plan actually shown (ВЦ lessons show the ГК plan).</summary>
    public static string Place(MapInfo map, Loc loc) => $"{(map.Building == "ВЦ" ? "ГК" : map.Building)}, {loc.T("mapFloorN", map.Floor)}";
}
