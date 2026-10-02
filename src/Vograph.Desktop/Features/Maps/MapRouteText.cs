namespace Vograph.Desktop.Features.Maps;

public static class MapRouteText
{
    public static string Format(IEnumerable<RouteStepItem> steps) => string.Join(Environment.NewLine,
        steps.Select((step, index) => $"{index + 1}. {step.Text}"));
}
