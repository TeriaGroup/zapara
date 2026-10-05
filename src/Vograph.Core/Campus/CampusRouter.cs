using System.Text.RegularExpressions;

namespace Vograph.Core.Campus;

public static class CampusRouter
{
    public static RouteResult Find(CampusGraph graph, string fromId, string toId)
    {
        var byId = new Dictionary<string, Node>(graph.Nodes.Count, StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
            byId.TryAdd(node.Id, node);

        if (fromId is null || toId is null
            || !byId.ContainsKey(fromId)
            || !byId.ContainsKey(toId))
            return RouteResult.Fail("unknown_place");

        if (fromId == toId)
            return RouteResult.Success(new Route(0, [], []));

        var adj = BuildAdjacency(graph);
        var startCost = new SearchCost(0, 0);
        var gScore = new Dictionary<string, SearchCost>(StringComparer.Ordinal) { [fromId] = startCost };
        var cameFrom = new Dictionary<string, CameFrom>(StringComparer.Ordinal);
        // Dijkstra: floor numbers alone cannot bound the cost of an authored building link.
        var open = new PriorityQueue<string, SearchPriority>();
        open.Enqueue(fromId, new SearchPriority(startCost, fromId));
        var closed = new HashSet<string>(StringComparer.Ordinal);

        while (open.TryDequeue(out var currentId, out _))
        {
            if (!closed.Add(currentId))
                continue;
            if (currentId == toId)
                return RouteResult.Success(BuildRoute(fromId, toId, byId, cameFrom, gScore[toId].Seconds));

            // A room may have several doors, but only corridors and connectors are transit places.
            if (currentId != fromId && byId[currentId].Kind == "room")
                continue;

            if (!adj.TryGetValue(currentId, out var links))
                continue;

            var currentG = gScore[currentId];
            foreach (var link in links)
            {
                if (closed.Contains(link.To))
                    continue;
                var tentative = new SearchCost(currentG.Seconds + link.Edge.Seconds,
                    currentG.Stairs + (link.Edge.Kind is "stair_up" or "stair_down" ? 1 : 0));
                if (gScore.TryGetValue(link.To, out var known) && tentative.CompareTo(known) >= 0)
                    continue;
                cameFrom[link.To] = new CameFrom(currentId, link.Edge, link.Reverse);
                gScore[link.To] = tentative;
                open.Enqueue(link.To, new SearchPriority(tentative, link.To));
            }
        }

        return RouteResult.Fail("unreachable");
    }

    public static RouteResult FindFromEntrance(CampusGraph graph, string entranceId, string roomKey)
    {
        var entrance = ResolveEntrance(graph, entranceId);
        var room = Resolve(graph, roomKey);
        if (entrance is null || room is null)
            return RouteResult.Fail("unknown_place");
        return Find(graph, entrance.Id, room.Id);
    }

    public static RouteResult FindResolved(CampusGraph graph, string fromToken, string toToken)
    {
        var from = Resolve(graph, fromToken);
        var to = Resolve(graph, toToken);
        if (from is null || to is null)
            return RouteResult.Fail("unknown_place");
        return Find(graph, from.Id, to.Id);
    }

    public static string RoomKey(string? classroomRaw)
    {
        if (string.IsNullOrWhiteSpace(classroomRaw)) return "";
        return classroomRaw.Trim().TrimEnd(';').Replace("*", "", StringComparison.Ordinal).Trim();
    }

    public static Node? Resolve(CampusGraph graph, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var key = RoomKey(token);
        if (key.Length == 0) return null;
        foreach (var node in graph.Nodes)
        {
            if (string.Equals(node.Id, token, StringComparison.Ordinal)
                || string.Equals(node.Id, key, StringComparison.Ordinal))
                return node;
        }
        Node? match = null;
        foreach (var node in graph.Nodes)
        {
            if (node.Room is null || !string.Equals(node.Room, key, StringComparison.Ordinal)) continue;
            if (match is not null) return null;
            match = node;
        }
        return match;
    }

    /// <summary>Schedule notation: an asterisk means УЛК; an unmarked classroom means ГК.</summary>
    public static Node? ResolveClassroom(CampusGraph graph, string? classroomRaw)
    {
        if (string.IsNullOrWhiteSpace(classroomRaw)) return null;
        var raw = classroomRaw.Trim().TrimEnd(';').Trim();
        foreach (var node in graph.Nodes)
        {
            if (string.Equals(node.Id, raw, StringComparison.Ordinal)) return node;
        }
        var building = raw.Contains("ВЦ", StringComparison.OrdinalIgnoreCase) || !raw.Contains('*') ? "ГК" : "УЛК";
        var key = RoomKey(raw);
        var vcRoom = Regex.Match(key, @"^ВЦ\s*([0-9]+[а-яa-z]?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (vcRoom.Success) key = vcRoom.Groups[1].Value;
        Node? match = null;
        foreach (var node in graph.Nodes)
        {
            if (node.Kind != "room" || node.Building != building
                || !string.Equals(node.Room, key, StringComparison.OrdinalIgnoreCase)) continue;
            if (match is not null) return null;
            match = node;
        }
        return match;
    }

    public static Node? ResolveEntrance(CampusGraph graph, string? entranceId)
    {
        if (string.IsNullOrWhiteSpace(entranceId)) return null;
        foreach (var node in graph.Nodes)
        {
            if (string.Equals(node.Id, entranceId, StringComparison.Ordinal) && node.Kind == "entrance")
                return node;
        }
        return null;
    }

    public static Node? ChooseFrom(CampusGraph graph, string? previousRoomKey, string? lastEntranceId)
    {
        if (Resolve(graph, previousRoomKey) is { } previous)
            return previous;
        return ResolveEntrance(graph, lastEntranceId);
    }

    public static IReadOnlyList<Node> Entrances(CampusGraph graph)
    {
        List<Node> list = [];
        foreach (var node in graph.Nodes)
        {
            if (node.Kind == "entrance")
                list.Add(node);
        }
        return list;
    }

    private static Dictionary<string, List<Link>> BuildAdjacency(CampusGraph graph)
    {
        var byId = new Dictionary<string, Node>(graph.Nodes.Count, StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
            byId.TryAdd(node.Id, node);

        var adj = new Dictionary<string, List<Link>>(StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (!byId.TryGetValue(edge.From, out var from) || !byId.TryGetValue(edge.To, out var to))
                continue;
            if (CampusBlocked.WalkBlocked(edge, from, to, graph.Blocked))
                continue;
            Add(adj, edge.From, new Link(edge.To, edge, Reverse: false));
            if (!edge.OneWay)
                Add(adj, edge.To, new Link(edge.From, edge, Reverse: true));
        }
        return adj;
    }

    private static void Add(Dictionary<string, List<Link>> adj, string from, Link link)
    {
        if (!adj.TryGetValue(from, out var links))
            adj[from] = links = [];
        links.Add(link);
    }

    private static Route BuildRoute(
        string fromId,
        string toId,
        Dictionary<string, Node> byId,
        Dictionary<string, CameFrom> cameFrom,
        double seconds)
    {
        var legs = new List<Leg>();
        for (var id = toId; id != fromId;)
        {
            var step = cameFrom[id];
            legs.Add(ToLeg(step.Edge, byId[step.FromId], byId[id], step.Reverse));
            id = step.FromId;
        }
        legs.Reverse();
        return new Route((int)Math.Round(seconds, MidpointRounding.AwayFromZero), legs, []);
    }

    private static Leg ToLeg(Edge edge, Node from, Node to, bool reverse)
    {
        var kind = TravelKind(edge.Kind, reverse);
        var points = PointsFor(edge, from, to, reverse);
        if (kind == "walk")
            return new Leg(kind, from.Building, from.Floor, null, null, points);
        return new Leg(kind, from.Building, from.Floor, to.Building, to.Floor, points);
    }

    private static string TravelKind(string kind, bool reverse) => reverse
        ? kind switch
        {
            "stair_up" => "stair_down",
            "stair_down" => "stair_up",
            _ => kind
        }
        : kind;

    private static IReadOnlyList<GraphPoint> PointsFor(Edge edge, Node from, Node to, bool reverse)
    {
        if (edge.Points is not { Count: > 0 } poly)
            return [new GraphPoint(from.X, from.Y), new GraphPoint(to.X, to.Y)];
        if (!reverse)
            return poly;
        var copy = new GraphPoint[poly.Count];
        for (var i = 0; i < poly.Count; i++)
            copy[i] = poly[poly.Count - 1 - i];
        return copy;
    }

    private readonly record struct Link(string To, Edge Edge, bool Reverse);

    private readonly record struct CameFrom(string FromId, Edge Edge, bool Reverse);

    private readonly record struct SearchCost(double Seconds, int Stairs) : IComparable<SearchCost>
    {
        public int CompareTo(SearchCost other)
        {
            var seconds = Seconds.CompareTo(other.Seconds);
            return seconds != 0 ? seconds : Stairs.CompareTo(other.Stairs);
        }
    }

    private readonly record struct SearchPriority(SearchCost Cost, string NodeId) : IComparable<SearchPriority>
    {
        public int CompareTo(SearchPriority other)
        {
            var cost = Cost.CompareTo(other.Cost);
            return cost != 0 ? cost : StringComparer.Ordinal.Compare(NodeId, other.NodeId);
        }
    }
}
