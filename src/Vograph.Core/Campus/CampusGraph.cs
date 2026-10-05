namespace Vograph.Core.Campus;

public sealed record Node(
    string Id,
    string Kind,
    string Building,
    int Floor,
    double X,
    double Y,
    string? Label = null,
    string? Room = null,
    string? Group = null);

public readonly record struct GraphPoint(double X, double Y);

public sealed record Edge(
    string From,
    string To,
    string Kind,
    double Seconds,
    bool OneWay,
    IReadOnlyList<GraphPoint>? Points = null);

public sealed record BlockedRegion(
    string Building,
    int Floor,
    double Left,
    double Top,
    double Right,
    double Bottom,
    string? OwnerId = null);

public sealed record CampusGraph(
    int Version,
    IReadOnlyList<string> Buildings,
    IReadOnlyList<Node> Nodes,
    IReadOnlyList<Edge> Edges,
    IReadOnlyList<BlockedRegion>? Blocked = null)
{
    public static CampusGraph Load(string json) => CampusGraphJson.Load(json);
}

public sealed class CampusGraphException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
