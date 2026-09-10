namespace Vograph.Core.Campus;

public sealed record RouteResult(bool Ok, Route? Route, string? Failure)
{
    public static RouteResult Success(Route route) => new(true, route, null);

    public static RouteResult Fail(string failure) => new(false, null, failure);
}

public sealed record Route(
    int Seconds,
    IReadOnlyList<Leg> Legs,
    IReadOnlyList<Step> Steps);

public sealed record Leg(
    string Kind,
    string Building,
    int Floor,
    string? ToBuilding,
    int? ToFloor,
    IReadOnlyList<GraphPoint> Points);

public sealed record Step(string Text);
