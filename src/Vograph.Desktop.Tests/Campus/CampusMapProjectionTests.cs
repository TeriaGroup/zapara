using Vograph.Core.Campus;
using Vograph.Desktop.Features.Maps;
using Xunit;

namespace Vograph.Desktop.Tests;

public class CampusMapProjectionTests
{
    [Fact]
    public void Singleton_stair_polyline_keeps_both_floor_markers_after_graph_loading_and_routing()
    {
        var graph = CampusGraph.Load("""
            {"version":1,"buildings":["УЛК"],"nodes":[
              {"id":"lower","kind":"stair","building":"УЛК","floor":1,"x":0.2,"y":0.3,"group":"shaft"},
              {"id":"upper","kind":"stair","building":"УЛК","floor":2,"x":0.2,"y":0.3,"group":"shaft"}
            ],"edges":[{"from":"lower","to":"upper","kind":"stair_up","seconds":20,"oneWay":false,"points":[[0.2,0.3]]}]}
            """);
        var route = CampusRouter.Find(graph, "lower", "upper").Route!;

        Assert.Single(Assert.Single(route.Legs).Points);
        Assert.Equal(new StairMarker(.2, .3, "↑ 2", true), Assert.Single(MapsComposer.StairMarkers(route, "УЛК", 1)));
        Assert.Equal(new StairMarker(.2, .3, "↑ с 1", false), Assert.Single(MapsComposer.StairMarkers(route, "УЛК", 2)));
    }

    [Theory]
    [InlineData("stair_up", 1, 2, 3, "↑ 3")]
    [InlineData("stair_down", 3, 2, 1, "↓ 1")]
    public void Stair_only_intermediate_floor_marks_onward_departure_at_actual_landing(
        string kind, int start, int middle, int end, string label)
    {
        var route = new Route(40, [
            new Leg(kind, "УЛК", start, "УЛК", middle, [new(.2, .3), new(.3, .4)]),
            new Leg(kind, "УЛК", middle, "УЛК", end, [new(.3, .4), new(.4, .5)])
        ], []);

        Assert.Empty(MapsComposer.FloorPathStrokes(route, "УЛК", middle));
        Assert.Equal(new StairMarker(.3, .4, label, true), Assert.Single(MapsComposer.StairMarkers(route, "УЛК", middle)));
        Assert.Equal(2, route.Legs.Count);
    }

    [Theory]
    [InlineData("stair_up", 1, 2, "↑ 2", "↑ с 1")]
    [InlineData("stair_down", 2, 1, "↓ 1", "↓ с 2")]
    public void Stair_markers_use_each_floors_own_endpoint_and_filter_buildings(
        string kind, int from, int to, string departure, string arrival)
    {
        var route = new Route(40, [
            new Leg(kind, "ГК", from, "ГК", to, [new(.2, .3), new(.4, .5)]),
            new Leg(kind, "УЛК", from, "УЛК", to, [new(.6, .7), new(.8, .9)])
        ], []);

        Assert.Equal(new StairMarker(.2, .3, departure, true), Assert.Single(MapsComposer.StairMarkers(route, "ГК", from)));
        Assert.Equal(new StairMarker(.4, .5, arrival, false), Assert.Single(MapsComposer.StairMarkers(route, "ГК", to)));
        Assert.Equal(new StairMarker(.8, .9, arrival, false), Assert.Single(MapsComposer.StairMarkers(route, "УЛК", to)));
        Assert.Empty(MapsComposer.StairMarkers(route, "ГК", 5));
    }

    [Fact]
    public void Revisited_floor_keeps_distinct_stairs_and_disconnected_walk_strokes()
    {
        var route = CampusRouter.Find(SyntheticLabyrinth.Build(), "lab.room.west.3", "lab.room.east.3").Route!;
        var markers = MapsComposer.StairMarkers(route, "УЛК", 3);

        Assert.Equal(new[] { new StairMarker(.2, .5, "↓ 2", true), new StairMarker(.8, .5, "↑ с 2", false) }, markers);
        var strokes = MapsComposer.FloorPathStrokes(route, "УЛК", 3);
        Assert.Equal(2, strokes.Count);
        Assert.DoesNotContain(strokes, points => points.Any(p => p.x < .3) && points.Any(p => p.x > .7));
    }

    [Theory]
    [InlineData("ГК", "УЛК")]
    [InlineData("УЛК", "ГК")]
    public void Yard_transfer_never_connects_coordinates_of_different_plans(string from, string to)
    {
        var route = new Route(30, [
            new Leg("walk", from, 1, null, null, [new(.2, .8), new(.3, .8)]),
            new Leg("building_link", from, 1, to, 1, [new(.3, .8), new(.7, .2)]),
            new Leg("walk", to, 1, null, null, [new(.7, .2), new(.8, .2)])
        ], []);

        Assert.Equal(new[] { (.2, .8), (.3, .8) }, Assert.Single(MapsComposer.FloorPathStrokes(route, from, 1)));
        Assert.Equal(new[] { (.7, .2), (.8, .2) }, Assert.Single(MapsComposer.FloorPathStrokes(route, to, 1)));
        Assert.Single(CampusStackProjector.RoutePolylines(route, from));
        Assert.Single(CampusStackProjector.RoutePolylines(route, to));
        Assert.Contains(RouteSteps.Format(route, new()), s => s.Contains(to));
    }
}
