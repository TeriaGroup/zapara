using System.Text.Json;
using System.Text.RegularExpressions;
using Vograph.Core.Campus;

namespace Zapara.Web.Components.Maps;

public sealed record MapFloor(string Building, int Floor)
{
    public string Key => $"{Building} {Floor}";
    public string Image => "maps/" + (Building == "ГК" ? $"karta-glavnyj-korpus-{Floor}-etazh-2022.jpg" : $"karta-ulk.-{Floor}-etazh-2022.jpg");
    public static IReadOnlyList<MapFloor> All { get; } = Enumerable.Range(1, 4).Select(f => new MapFloor("ГК", f))
        .Concat(Enumerable.Range(1, 5).Select(f => new MapFloor("УЛК", f))).ToArray();
}
public sealed record RoomMark(MapFloor Plan, string Room, double X, double Y, double Width, double Height);
public sealed record MapLocation(MapFloor? Plan, string Room, Node? Node, RoomMark? Highlight, string? Problem);
public sealed record MapRouteStep(int Index, string Text, MapFloor From, MapFloor To, IReadOnlyList<int> Legs);
public sealed record MapRouteMarker(MapFloor Plan, double X, double Y, string Label, int Step);

public static class MapGeometry
{
    public static IReadOnlyList<RoomMark> ParseCoordinates(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Неизвестная версия координат.");
            var result = new List<RoomMark>();
            foreach (var floor in document.RootElement.GetProperty("maps").EnumerateObject())
            {
                var plan = MapFloor.All.SingleOrDefault(p => p.Key == floor.Name) ?? throw new InvalidDataException("Неизвестный план.");
                foreach (var room in floor.Value.EnumerateObject())
                {
                    var r = room.Value;
                    var mark = new RoomMark(plan, room.Name, r.GetProperty("x").GetDouble(), r.GetProperty("y").GetDouble(), r.GetProperty("w").GetDouble(), r.GetProperty("h").GetDouble());
                    if (!Valid(mark.X, mark.Y) || mark.Width is <= 0 or > 1 || mark.Height is <= 0 or > 1) throw new InvalidDataException("Некорректные координаты.");
                    result.Add(mark);
                }
            }
            return result;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("Не удалось прочитать координаты карты.", error); }
    }

    public static MapLocation Resolve(CampusGraph graph, IReadOnlyList<RoomMark> marks, string raw)
    {
        var text = raw.Trim().TrimEnd(';').Trim();
        if (text.Contains("дистанционно", StringComparison.OrdinalIgnoreCase)) return new(null, text, null, null, "remote");
        if (text.Contains(';')) return new(null, text, null, null, "ambiguous");
        var match = Regex.Match(text, @"^(?:(ГК|УЛК|ВЦ)\s*)?(\d{1,4}[а-яa-z]?)\s*(\*)?$", RegexOptions.IgnoreCase);
        if (!match.Success) return new(null, text, null, null, "unknown");
        var prefix = match.Groups[1].Value;
        var building = prefix.Equals("УЛК", StringComparison.OrdinalIgnoreCase) ? "УЛК"
            : prefix.Equals("ВЦ", StringComparison.OrdinalIgnoreCase) || prefix.Equals("ГК", StringComparison.OrdinalIgnoreCase) ? "ГК"
            : match.Groups[3].Success ? "УЛК" : "ГК";
        var room = match.Groups[2].Value;
        var nodes = graph.Nodes.Where(n => n.Kind == "room" && n.Building == building && room.Equals(n.Room, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (nodes.Length > 1) return new(null, room, null, null, "ambiguous");
        var node = nodes.SingleOrDefault();
        var matching = marks.Where(m => m.Plan.Building == building && m.Room.Equals(room, StringComparison.OrdinalIgnoreCase)).ToArray();
        var floor = node?.Floor ?? matching.FirstOrDefault()?.Plan.Floor ?? room[0] - '0';
        var plan = MapFloor.All.SingleOrDefault(p => p.Building == building && p.Floor == floor);
        if (plan is null) return new(null, room, node, null, "unknown");
        var highlight = matching.FirstOrDefault(m => m.Plan == plan);
        return new(plan, room, node, highlight, highlight is null && node is null ? "unmarked" : null);
    }

    public static IReadOnlyList<MapRouteStep> Steps(Route route)
    {
        var result = new List<MapRouteStep>();
        for (var i = 0; i < route.Legs.Count; i++)
        {
            var leg = route.Legs[i];
            var from = new MapFloor(leg.Building, leg.Floor);
            var to = new MapFloor(leg.ToBuilding ?? leg.Building, leg.ToFloor ?? leg.Floor);
            if (leg.Kind == "walk" && i > 0 && route.Legs[i - 1].Kind == "walk" && result[^1].From == from)
            { result[^1] = result[^1] with { Legs = result[^1].Legs.Append(i).ToArray() }; continue; }
            var text = leg.Kind switch
            {
                "walk" => $"Пройдите по коридору · {from.Key} этаж",
                "stair_up" => $"Поднимитесь на {to.Floor} этаж · {to.Building}",
                "stair_down" => $"Спуститесь на {to.Floor} этаж · {to.Building}",
                "building_link" => $"Перейдите в корпус {to.Building} · {to.Floor} этаж",
                _ => "Участок маршрута не поддерживается"
            };
            result.Add(new(result.Count, text, from, to, [i]));
        }
        return result;
    }

    public static IReadOnlyList<Leg> Strokes(Route route, MapFloor plan) => route.Legs.Where(l => l.Building == plan.Building && l.Floor == plan.Floor
        && (l.Kind == "walk" || l.Kind == "building_link" && l.ToBuilding == l.Building && l.ToFloor == l.Floor)
        && l.Points.Count > 1 && l.Points.All(p => Valid(p.X, p.Y))).ToArray();

    public static IReadOnlyList<MapRouteMarker> Markers(Route route)
    {
        var result = new List<MapRouteMarker>();
        foreach (var step in Steps(route))
        {
            var leg = route.Legs[step.Legs[0]];
            if (leg.Kind == "walk" || leg.Points.Count == 0 || leg.Points.Any(p => !Valid(p.X, p.Y))) continue;
            var first = leg.Points[0]; var last = leg.Points[^1];
            result.Add(new(step.From, first.X, first.Y, leg.Kind == "building_link" ? $"В {step.To.Building}" : $"{(leg.Kind == "stair_up" ? "↑" : "↓")} {step.To.Floor}", step.Index));
            result.Add(new(step.To, last.X, last.Y, leg.Kind == "building_link" ? $"Из {step.From.Building}" : $"С {step.From.Floor}", step.Index));
        }
        return result;
    }

    private static bool Valid(double x, double y) => double.IsFinite(x) && double.IsFinite(y) && x is >= 0 and <= 1 && y is >= 0 and <= 1;
}
