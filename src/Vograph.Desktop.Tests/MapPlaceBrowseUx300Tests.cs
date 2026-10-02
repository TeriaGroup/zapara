using Vograph.Core.Campus;
using Vograph.Desktop.Features.Maps;
using Xunit;

namespace Vograph.Desktop.Tests;

public class MapPlaceBrowseUx300Tests
{
    [Fact]
    public void Same_room_number_in_two_buildings_stays_distinct()
    {
        Node[] nodes =
        [
            new("gk-301", "room", "ГК", 3, .2, .3, Room: "301"),
            new("ulk-301", "room", "УЛК", 3, .4, .5, Room: "301")
        ];

        Assert.Equal(2, MapPlaceBrowse.Filter(nodes, "301").Count);
        Assert.Equal("УЛК", Assert.Single(MapPlaceBrowse.Filter(nodes, "улк 301")).Building);
    }

    [Fact]
    public async Task Manual_start_and_destination_recompute_route_from_chosen_rooms()
    {
        using var db = TestDb.Create();
        var vm = new MapsViewModel(db.Services, new Vograph.Desktop.Shell.ShellViewModel(db.Services));
        vm.PlaceSearch = "493";
        var start = vm.PlaceResults.Single(place => place.Building == "ГК" && place.Room == "493");
        vm.PlaceSearch = "320";
        var destination = vm.PlaceResults.Single(place => place.Building == "УЛК" && place.Room == "320");

        vm.SelectRouteStartCommand.Execute(start);
        await vm.SelectRouteDestinationCommand.ExecuteAsync(destination);

        Assert.Contains("493", vm.ManualStartCaption);
        Assert.Contains("320", vm.ManualDestinationCaption);
        Assert.NotNull(vm.Route);
        Assert.NotEmpty(vm.RouteSteps);
        Assert.Contains(vm.RouteSteps[0].Text, vm.RouteStepHeader);
        if (vm.RouteSteps.Count > 1)
        {
            vm.AdvanceRouteStepCommand.Execute(null);
            Assert.Contains(vm.RouteSteps[1].Text, vm.RouteStepHeader);
            vm.BackRouteStepCommand.Execute(null);
            Assert.Contains(vm.RouteSteps[0].Text, vm.RouteStepHeader);
        }

        await vm.ShowRouteStartCommand.ExecuteAsync(null);
        Assert.Equal(("ГК", 4), (vm.Current?.Building, vm.Current?.Floor));
        await vm.ShowRouteDestinationCommand.ExecuteAsync(null);
        Assert.Equal(("УЛК", 3), (vm.Current?.Building, vm.Current?.Floor));
        Assert.All(vm.CurrentFloorPlaces, place => Assert.Equal(("УЛК", 3), (place.Building, place.Floor)));
        Assert.Contains(vm.CurrentFloorPlaces, place => place.Room == "320");
        var recentRoute = Assert.Single(vm.RecentRoutes);
        vm.PinPlaceCommand.Execute(start);
        Assert.Equal(start.Id, Assert.Single(vm.PinnedPlaces).Id);
        Assert.Equal(2, vm.RecentPlaces.Count);
        var visibleMap = vm.Current;
        vm.ClearRecentPlacesCommand.Execute(null);
        Assert.Empty(vm.RecentPlaces);
        Assert.Single(vm.PinnedPlaces);
        Assert.Same(visibleMap, vm.Current);

        vm.ClearManualRouteCommand.Execute(null);
        Assert.False(vm.HasManualRoutePoints);
        Assert.Null(vm.Route);
        await vm.RepeatRecentRouteCommand.ExecuteAsync(recentRoute);
        Assert.NotNull(vm.Route);
        vm.ClearRecentRoutesCommand.Execute(null);
        Assert.Empty(vm.RecentRoutes);
        Assert.NotNull(vm.Route);
        vm.UnpinPlaceCommand.Execute(start);
        Assert.Empty(vm.PinnedPlaces);
    }

    [Fact]
    public async Task Offline_readiness_lists_each_building_and_floor()
    {
        using var db = TestDb.Create();
        var vm = new MapsViewModel(db.Services, new Vograph.Desktop.Shell.ShellViewModel(db.Services));

        await vm.ActivateAsync();

        Assert.True(vm.OfflinePlans.Count >= 9);
        Assert.Contains(vm.OfflinePlans, plan => plan.Label.Contains("ГК"));
        Assert.Contains(vm.OfflinePlans, plan => plan.Label.Contains("УЛК"));
    }
}
