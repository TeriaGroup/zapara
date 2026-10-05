using Vograph.Core.Campus;
using Xunit;

namespace Zapara.Client.Domain.Tests;

public class CampusTests
{
    [Fact]
    public void Steps_merge_walks_and_follow_transition_destination()
    {
        var steps = CampusRouteSteps.Format(new Route(90,
            [new("walk", "ГК", 1, null, null, []), new("walk", "ГК", 1, null, null, []),
             new("stair_up", "ГК", 1, "ГК", 2, []), new("building_link", "ГК", 2, "УЛК", 3, [])], []));
        Assert.Equal(3, steps.Count);
        Assert.Equal(("ГК", 2), (steps[1].Building, steps[1].Floor));
        Assert.Equal(("УЛК", 3), (steps[2].Building, steps[2].Floor));
        Assert.Contains("Поднимитесь", steps[1].Text);
    }

    [Fact]
    public void Real_bundled_graph_routes_between_buildings_and_rejects_unmapped_room()
    {
        var graph = CampusGraph.Load(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "campus-graph.json")));
        var route = CampusRouter.Find(graph, "ulk.room.320", "gk.room.493");
        Assert.True(route.Ok);
        Assert.Contains(route.Route!.Legs, l => l.Kind == "building_link");
        Assert.True(route.Route.Seconds > 0);
        Assert.Equal("unknown_place", CampusRouter.FindResolved(graph, "несуществующая", "493").Failure);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":2,\"buildings\":[],\"nodes\":[],\"edges\":[]}")]
    public void Malformed_or_unknown_version_map_is_rejected(string json) => Assert.Throws<CampusGraphException>(() => CampusGraph.Load(json));
}
