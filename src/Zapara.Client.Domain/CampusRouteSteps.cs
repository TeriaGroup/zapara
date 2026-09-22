using Vograph.Core.Campus;

namespace Zapara.Client.Domain;

public sealed record CampusRouteStep(string Text, string Building, int Floor);

public static class CampusRouteSteps
{
    public static IReadOnlyList<CampusRouteStep> Format(Route route)
    {
        var steps = new List<CampusRouteStep>();
        Leg? previous = null;
        foreach (var leg in route.Legs)
        {
            var continues = leg.Kind == "walk" && previous is { Kind: "walk" }
                && leg.Building == previous.Building && leg.Floor == previous.Floor;
            previous = leg;
            if (continues) continue;
            var text = leg.Kind switch
            {
                "stair_down" => $"Спуститесь на {leg.ToFloor ?? leg.Floor} этаж",
                "stair_up" => $"Поднимитесь на {leg.ToFloor ?? leg.Floor} этаж",
                "walk" => $"Пройдите по коридору ({leg.Floor} этаж)",
                "building_link" => $"Перейдите в корпус {leg.ToBuilding ?? leg.Building}, {leg.ToFloor ?? leg.Floor} этаж",
                _ => null
            };
            if (text is not null) steps.Add(new(text, leg.ToBuilding ?? leg.Building, leg.ToFloor ?? leg.Floor));
        }
        return steps;
    }
}
