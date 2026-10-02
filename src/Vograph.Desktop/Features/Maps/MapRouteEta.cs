namespace Vograph.Desktop.Features.Maps;

public static class MapRouteEta
{
    public static string Format(int seconds) => seconds <= 0 ? "" : $"около {Math.Max(1, (int)Math.Ceiling(seconds / 60d))} мин";
}
