using Vograph.Core.Campus;
using Xunit;

namespace Vograph.Desktop.Tests;

public class CampusGraphContinuityTests
{
    [Fact]
    public void Matching_stair_group_cannot_connect_different_buildings()
    {
        var json = """
            {"version":1,"buildings":["ГК","УЛК"],"nodes":[
              {"id":"a","kind":"stair","building":"ГК","floor":1,"x":0.1,"y":0.2,"group":"shared"},
              {"id":"b","kind":"stair","building":"УЛК","floor":2,"x":0.5,"y":0.6,"group":"shared"}
            ],"edges":[{"from":"a","to":"b","kind":"stair_up","seconds":20,"oneWay":false}]}
            """;
        Assert.Equal("bad_stair", Assert.Throws<CampusGraphException>(() => CampusGraph.Load(json)).Code);
    }

    [Fact]
    public void Blank_stair_group_cannot_identify_a_shaft()
    {
        var json = """
            {"version":1,"buildings":["ГК"],"nodes":[
              {"id":"a","kind":"stair","building":"ГК","floor":1,"x":0.1,"y":0.2,"group":" "},
              {"id":"b","kind":"landing","building":"ГК","floor":2,"x":0.5,"y":0.6,"group":" "}
            ],"edges":[{"from":"a","to":"b","kind":"stair_up","seconds":20,"oneWay":false}]}
            """;
        Assert.Equal("bad_stair", Assert.Throws<CampusGraphException>(() => CampusGraph.Load(json)).Code);
    }

    [Theory]
    [InlineData("[[0.1,0.2]]")]
    [InlineData("[[0.2,0.2],[0.5,0.6]]")]
    [InlineData("[[0.1,0.2],[0.5,0.5]]")]
    [InlineData("[[0.5,0.6],[0.1,0.2]]")]
    public void Explicit_polylines_cannot_disappear_or_jump_between_graph_nodes(string points)
    {
        Assert.Equal("bad_edge_geometry", Assert.Throws<CampusGraphException>(() => CampusGraph.Load(Walk(points))).Code);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    public void Unspecified_geometry_keeps_the_endpoint_fallback(string points)
    {
        var route = CampusRouter.Find(CampusGraph.Load(Walk(points)), "a", "b").Route!;
        Assert.Equal(new[] { new GraphPoint(.1, .2), new GraphPoint(.5, .6) }, Assert.Single(route.Legs).Points);
    }

    [Fact]
    public void Explicit_polylines_allow_subpixel_coordinate_rounding()
    {
        var graph = CampusGraph.Load(Walk("[[0.1000001,0.2],[0.3,0.4],[0.4999999,0.6]]"));
        Assert.True(CampusRouter.Find(graph, "a", "b").Ok);
    }

    [Fact]
    public void Coincident_connector_nodes_do_not_require_artificial_movement()
    {
        var graph = CampusGraph.Load(Walk("[[0.1,0.2]]")
            .Replace("\"x\":0.5,\"y\":0.6", "\"x\":0.1,\"y\":0.2", StringComparison.Ordinal));
        Assert.True(CampusRouter.Find(graph, "a", "b").Ok);
    }

    private static string Walk(string points) => $$"""
        {"version":1,"buildings":["ГК"],"nodes":[
          {"id":"a","kind":"junction","building":"ГК","floor":1,"x":0.1,"y":0.2},
          {"id":"b","kind":"room","building":"ГК","floor":1,"x":0.5,"y":0.6,"room":"101"}
        ],"edges":[{"from":"a","to":"b","kind":"walk","seconds":10,"oneWay":false,"points":{{points}}}]}
        """;
}
