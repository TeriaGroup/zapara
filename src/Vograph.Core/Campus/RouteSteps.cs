using Vograph.Core.Services;

namespace Vograph.Core.Campus;

public sealed record FormattedRouteStep(string Text, string Building, int Floor);

public static class RouteSteps
{
    public static IReadOnlyList<string> Format(Route route, I18nService i18n) =>
        FormatWithLocations(route, i18n).Select(step => step.Text).ToArray();

    public static IReadOnlyList<FormattedRouteStep> FormatWithLocations(Route route, I18nService i18n)
    {
        var steps = new List<FormattedRouteStep>(route.Legs.Count);
        Leg? previous = null;
        foreach (var leg in route.Legs)
        {
            var continuesWalk = leg.Kind == "walk" && previous is { Kind: "walk" }
                && leg.Building == previous.Building && leg.Floor == previous.Floor;
            previous = leg;
            if (continuesWalk) continue;
            var text = TextFor(leg, i18n);
            if (text is not null)
                steps.Add(new FormattedRouteStep(text, leg.ToBuilding ?? leg.Building, leg.ToFloor ?? leg.Floor));
        }
        return steps;
    }

    private static string? TextFor(Leg leg, I18nService i18n) => leg.Kind switch
    {
        "stair_down" => i18n.T("routeStairDown", leg.ToFloor ?? leg.Floor),
        "stair_up" => i18n.T("routeStairUp", leg.ToFloor ?? leg.Floor),
        "walk" => i18n.T("routeWalk", leg.Floor),
        "building_link" => i18n.T("routeLink", leg.ToBuilding ?? leg.Building, leg.ToFloor ?? leg.Floor),
        _ => null
    };
}
