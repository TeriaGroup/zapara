using Vograph.Core.Campus;
using Zapara.Web.Components.Maps;
using Xunit;

namespace Zapara.Client.Domain.Tests;

public sealed class MapGeometryTests
{
    private const string Coords = """{"version":1,"maps":{"ГК 4":{"493":{"x":0.2,"y":0.3,"w":0.05,"h":0.04}},"УЛК 3":{"320":{"x":0.4,"y":0.5,"w":0.05,"h":0.04}}}}""";
    private static readonly CampusGraph Graph = new(1, ["ГК", "УЛК"],
        [new("gk.493", "room", "ГК", 4, .2, .3, Room: "493"), new("ulk.320", "room", "УЛК", 3, .4, .5, Room: "320")], []);

    [Fact]
    public void Original_bundled_coordinates_cover_all_nine_plans_and_resolve_real_classrooms()
    {
        var graph = CampusGraph.Load(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "campus-graph.json")));
        var marks = MapGeometry.ParseCoordinates(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "coords.json")));
        Assert.Equal(9, marks.Select(m => m.Plan).Distinct().Count());
        Assert.Equal("gk.room.493", MapGeometry.Resolve(graph, marks, "493").Node?.Id);
        Assert.Equal("ulk.room.320", MapGeometry.Resolve(graph, marks, "320*").Node?.Id);
        Assert.NotNull(MapGeometry.Resolve(graph, marks, "493").Highlight);
    }

    [Fact]
    public void Search_uses_original_classroom_building_notation_and_keeps_unmarked_distinct()
    {
        var marks = MapGeometry.ParseCoordinates(Coords);
        Assert.Equal(new MapFloor("ГК", 4), MapGeometry.Resolve(Graph, marks, "493;").Plan);
        Assert.Equal("ulk.320", MapGeometry.Resolve(Graph, marks, "320*;").Node?.Id);
        Assert.Equal(new MapFloor("УЛК", 3), MapGeometry.Resolve(Graph, marks, "УЛК 320").Plan);
        Assert.Equal(new MapFloor("ГК", 4), MapGeometry.Resolve(Graph, marks, "ВЦ 493*").Plan);
        Assert.Equal("remote", MapGeometry.Resolve(Graph, marks, "дистанционно;").Problem);
        Assert.Equal("ambiguous", MapGeometry.Resolve(Graph, marks, "493; 320*").Problem);
        Assert.Equal("unmarked", MapGeometry.Resolve(Graph, marks, "494").Problem);
        Assert.Equal("unknown", MapGeometry.Resolve(Graph, marks, "не аудитория").Problem);
    }

    [Theory]
    [InlineData("{\"version\":2,\"maps\":{}}")]
    [InlineData("{\"version\":1,\"maps\":{\"ГК 4\":{\"493\":{\"x\":2,\"y\":0.3,\"w\":0.1,\"h\":0.1}}}}")]
    public void Invalid_version_or_geometry_cannot_place_markers(string json) => Assert.Throws<InvalidDataException>(() => MapGeometry.ParseCoordinates(json));

    [Fact]
    public void Routes_keep_transition_endpoints_on_their_own_plans_without_diagonal_fabrication()
    {
        var route = new Route(100,
            [new("walk", "ГК", 1, null, null, [new(.1, .2), new(.3, .4)]),
             new("walk", "ГК", 1, null, null, [new(.3, .4), new(.5, .5)]),
             new("stair_up", "ГК", 1, "ГК", 2, [new(.5, .5), new(.6, .6)]),
             new("building_link", "ГК", 2, "УЛК", 3, [new(.7, .7), new(.8, .8)])], []);
        var steps = MapGeometry.Steps(route);
        Assert.Equal(3, steps.Count);
        Assert.Equal(new[] { 0, 1 }, steps[0].Legs);
        Assert.Equal(2, MapGeometry.Strokes(route, new("ГК", 1)).Count);
        Assert.Empty(MapGeometry.Strokes(route, new("УЛК", 3)));
        var markers = MapGeometry.Markers(route);
        Assert.Equal(4, markers.Count);
        Assert.Contains(markers, m => m.Plan == new MapFloor("УЛК", 3) && m.X == .8 && m.Y == .8);
    }
}
