using Vograph.Desktop.Features.Maps;
using Xunit;

namespace Vograph.Desktop.Tests;

public class MapRouteTextUx300Tests
{
    [Fact]
    public void Route_text_uses_current_steps_in_order_without_internal_ids()
    {
        RouteStepItem[] steps = [new("Войдите в ГК", "ГК", 1), new("Поднимитесь на 3 этаж", "ГК", 3)];

        var result = MapRouteText.Format(steps);

        Assert.Contains("1. Войдите в ГК", result);
        Assert.Contains("2. Поднимитесь на 3 этаж", result);
        Assert.DoesNotContain("Id", result);
        Assert.Equal("", MapRouteText.Format([]));
    }

    [Fact]
    public void Missing_destination_and_unknown_room_have_distinct_route_guidance()
    {
        using var db = TestDb.Create();
        var shell = new Vograph.Desktop.Shell.ShellViewModel(db.Services);
        var vm = new MapsViewModel(db.Services, shell);

        vm.SetRouteEnds(null, null);
        var missing = vm.RouteUnmarked;
        vm.SetRouteEnds("несуществующая аудитория", null);

        Assert.NotEqual(missing, vm.RouteUnmarked);
        Assert.Contains("аудитор", vm.RouteUnmarked, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(30, "около 1 мин")]
    [InlineData(121, "около 3 мин")]
    public void Estimated_route_minutes_round_up(int seconds, string expected)
    {
        Assert.Equal(expected, MapRouteEta.Format(seconds));
    }
}
