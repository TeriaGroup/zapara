using System.Text.Json;

namespace Vograph.Core.Campus;

internal static class CampusGraphJson
{
    private static readonly HashSet<string> AllowedBuildings = new(StringComparer.Ordinal) { "ГК", "УЛК" };
    private static readonly HashSet<string> NodeKinds = new(StringComparer.Ordinal)
    {
        "entrance", "room", "stair", "landing", "junction", "building_link"
    };
    private static readonly HashSet<string> EdgeKinds = new(StringComparer.Ordinal)
    {
        "walk", "stair_up", "stair_down", "building_link"
    };
    private static readonly HashSet<string> VerticalNodeKinds = new(StringComparer.Ordinal)
    {
        "stair", "landing"
    };

    internal static CampusGraph Load(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw InvalidJson();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw InvalidJson();
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw InvalidJson();

            if (Int(root, "version") != 1)
                throw InvalidJson();

            var buildingsEl = Array(root, "buildings");
            var buildings = new List<string>(buildingsEl.GetArrayLength());
            var buildingSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in buildingsEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    throw InvalidJson();
                var name = item.GetString()!;
                if (!AllowedBuildings.Contains(name) || !buildingSet.Add(name))
                    throw InvalidJson();
                buildings.Add(name);
            }

            var nodesEl = Array(root, "nodes");
            var nodes = new List<Node>(nodesEl.GetArrayLength());
            var byId = new Dictionary<string, Node>(StringComparer.Ordinal);
            foreach (var item in nodesEl.EnumerateArray())
            {
                var node = ReadNode(item, buildingSet);
                if (!byId.TryAdd(node.Id, node))
                    throw InvalidJson();
                nodes.Add(node);
            }

            var edgesEl = Array(root, "edges");
            var edges = new List<Edge>(edgesEl.GetArrayLength());
            foreach (var item in edgesEl.EnumerateArray())
                edges.Add(ReadEdge(item, byId));

            var blocked = ReadBlocked(root, buildingSet, byId);
            return new CampusGraph(1, buildings.ToArray(), nodes.ToArray(), edges.ToArray(), blocked);
        }
    }

    private static BlockedRegion[] ReadBlocked(
        JsonElement root, HashSet<string> buildings, Dictionary<string, Node> byId)
    {
        if (!root.TryGetProperty("blocked", out var blockedEl) || blockedEl.ValueKind == JsonValueKind.Null)
            return [];
        if (blockedEl.ValueKind != JsonValueKind.Array)
            throw InvalidJson();

        var blocked = new List<BlockedRegion>(blockedEl.GetArrayLength());
        foreach (var item in blockedEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw InvalidJson();
            var building = Text(item, "building");
            if (!buildings.Contains(building))
                throw InvalidJson();
            var floor = Int(item, "floor");
            if (floor is < 1 or > 5)
                throw InvalidJson();
            var left = Number(item, "left");
            var top = Number(item, "top");
            var right = Number(item, "right");
            var bottom = Number(item, "bottom");
            if (left is < 0 or > 1 || top is < 0 or > 1 || right is < 0 or > 1 || bottom is < 0 or > 1
                || left >= right || top >= bottom)
                throw InvalidJson();
            var owner = OptionalText(item, "owner");
            if (owner is not null && !byId.ContainsKey(owner))
                throw InvalidJson();
            blocked.Add(new BlockedRegion(building, floor, left, top, right, bottom, owner));
        }
        return blocked.ToArray();
    }

    private static Node ReadNode(JsonElement el, HashSet<string> buildings)
    {
        if (el.ValueKind != JsonValueKind.Object)
            throw InvalidJson();

        var id = Text(el, "id");
        if (string.IsNullOrWhiteSpace(id))
            throw InvalidJson();

        var kind = Text(el, "kind");
        if (!NodeKinds.Contains(kind))
            throw InvalidJson();

        var building = Text(el, "building");
        if (!buildings.Contains(building))
            throw InvalidJson();

        var floor = Int(el, "floor");
        if (floor is < 1 or > 5)
            throw InvalidJson();

        var x = Number(el, "x");
        var y = Number(el, "y");
        if (x is < 0 or > 1 || y is < 0 or > 1)
            throw InvalidJson();

        return new Node(id, kind, building, floor, x, y,
            OptionalText(el, "label"), OptionalText(el, "room"), OptionalText(el, "group"));
    }

    private static Edge ReadEdge(JsonElement el, Dictionary<string, Node> byId)
    {
        if (el.ValueKind != JsonValueKind.Object)
            throw InvalidJson();

        var fromId = Text(el, "from");
        var toId = Text(el, "to");
        var kind = Text(el, "kind");
        if (!EdgeKinds.Contains(kind))
            throw InvalidJson();

        var seconds = Number(el, "seconds");
        if (seconds <= 0 || !double.IsFinite(seconds))
            throw InvalidJson();

        var oneWay = Bool(el, "oneWay");
        var points = ReadPoints(el);

        if (!byId.TryGetValue(fromId, out var from) || !byId.TryGetValue(toId, out var to))
            throw new CampusGraphException("unknown_node", "Ребро ссылается на неизвестный узел.");

        switch (kind)
        {
            case "walk":
                if (from.Building != to.Building || from.Floor != to.Floor)
                    throw new CampusGraphException("bad_edge_floor",
                        "Пешеходное ребро должно соединять узлы одного корпуса и этажа.");
                break;
            case "stair_up":
            case "stair_down":
                ValidateStair(from, to, kind);
                break;
            case "building_link":
                if (from.Kind != "building_link" || to.Kind != "building_link")
                    throw InvalidJson();
                break;
        }

        ValidateGeometry(points, from, to);
        return new Edge(fromId, toId, kind, seconds, oneWay, points);
    }

    private static void ValidateStair(Node from, Node to, string kind)
    {
        if (from.Building != to.Building)
            throw BadStair();
        if (!VerticalNodeKinds.Contains(from.Kind) || !VerticalNodeKinds.Contains(to.Kind))
            throw BadStair();
        if (string.IsNullOrWhiteSpace(from.Group) || from.Group != to.Group)
            throw BadStair();
        var delta = to.Floor - from.Floor;
        if (Math.Abs(delta) != 1)
            throw BadStair();
        if (kind == "stair_up" && delta != 1)
            throw BadStair();
        if (kind == "stair_down" && delta != -1)
            throw BadStair();
    }

    private static void ValidateGeometry(IReadOnlyList<GraphPoint>? points, Node from, Node to)
    {
        // Absent/empty geometry uses the node-to-node fallback. Authored geometry must be continuous.
        if (points is not { Count: > 0 }) return;
        // A single point is valid only for a stationary connector whose two nodes coincide.
        if (!Matches(points[0], from) || !Matches(points[^1], to))
            throw new CampusGraphException("bad_edge_geometry", "Линия ребра должна соединять координаты его узлов.");

        static bool Matches(GraphPoint point, Node node) =>
            Math.Abs(point.X - node.X) <= 0.000001 && Math.Abs(point.Y - node.Y) <= 0.000001;
    }

    private static IReadOnlyList<GraphPoint>? ReadPoints(JsonElement el)
    {
        if (!el.TryGetProperty("points", out var pointsEl) || pointsEl.ValueKind == JsonValueKind.Null)
            return null;
        if (pointsEl.ValueKind != JsonValueKind.Array)
            throw InvalidJson();

        var points = new List<GraphPoint>(pointsEl.GetArrayLength());
        foreach (var item in pointsEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 2)
                throw InvalidJson();
            var x = FiniteNumber(item[0]);
            var y = FiniteNumber(item[1]);
            if (x is < 0 or > 1 || y is < 0 or > 1)
                throw InvalidJson();
            points.Add(new GraphPoint(x, y));
        }
        return points;
    }

    private static JsonElement Field(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            throw InvalidJson();
        return value;
    }

    private static JsonElement Array(JsonElement obj, string name)
    {
        var value = Field(obj, name);
        if (value.ValueKind != JsonValueKind.Array)
            throw InvalidJson();
        return value;
    }

    private static string Text(JsonElement obj, string name)
    {
        var value = Field(obj, name);
        if (value.ValueKind != JsonValueKind.String)
            throw InvalidJson();
        return value.GetString()!;
    }

    private static string? OptionalText(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw InvalidJson();
        return value.GetString();
    }

    private static int Int(JsonElement obj, string name)
    {
        var value = Field(obj, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var n))
            throw InvalidJson();
        return n;
    }

    private static double Number(JsonElement obj, string name)
    {
        var value = Field(obj, name);
        return FiniteNumber(value);
    }

    private static double FiniteNumber(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var n) || !double.IsFinite(n))
            throw InvalidJson();
        return n;
    }

    private static bool Bool(JsonElement obj, string name)
    {
        var value = Field(obj, name);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw InvalidJson();
        return value.GetBoolean();
    }

    private static CampusGraphException InvalidJson() =>
        new("invalid_json", "Некорректный JSON графа кампуса.");

    private static CampusGraphException BadStair() =>
        new("bad_stair", "Лестничное ребро должно соединять соседние этажи одной шахты.");
}
