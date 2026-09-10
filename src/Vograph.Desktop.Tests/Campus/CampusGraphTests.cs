using Vograph.Core.Campus;
using Xunit;

namespace Vograph.Desktop.Tests;

public class CampusGraphTests
{
    [Fact]
    public void Walk_edge_rejects_different_floors()
    {
        var json = """
        {"version":1,"buildings":["УЛК"],"nodes":[
          {"id":"a","kind":"junction","building":"УЛК","floor":1,"x":0.1,"y":0.1},
          {"id":"b","kind":"junction","building":"УЛК","floor":2,"x":0.1,"y":0.1}
        ],"edges":[{"from":"a","to":"b","kind":"walk","seconds":10,"oneWay":false}]}
        """;
        var ex = Assert.Throws<CampusGraphException>(() => CampusGraph.Load(json));
        Assert.Equal("bad_edge_floor", ex.Code);
    }

    [Fact]
    public void Stair_requires_same_group_and_adjacent_floors()
    {
        var json = """
        {"version":1,"buildings":["УЛК"],"nodes":[
          {"id":"s1","kind":"stair","building":"УЛК","floor":1,"x":0.2,"y":0.2,"group":"s"},
          {"id":"s3","kind":"stair","building":"УЛК","floor":3,"x":0.2,"y":0.2,"group":"s"}
        ],"edges":[{"from":"s1","to":"s3","kind":"stair_up","seconds":20,"oneWay":false}]}
        """;
        var ex = Assert.Throws<CampusGraphException>(() => CampusGraph.Load(json));
        Assert.Equal("bad_stair", ex.Code);
    }

    [Fact]
    public void Empty_graph_loads()
    {
        var graph = CampusGraph.Load("""{"version":1,"buildings":["УЛК"],"nodes":[],"edges":[]}""");
        Assert.Equal(1, graph.Version);
        Assert.Equal(["УЛК"], graph.Buildings);
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
        Assert.True(graph.Blocked is null || graph.Blocked.Count == 0);
    }

    [Fact]
    public void Load_reads_blocked_keep_outs_and_routes_around_them()
    {
        var graph = CampusGraph.Load("""
            {"version":1,"buildings":["ГК"],"nodes":[
              {"id":"start","kind":"junction","building":"ГК","floor":1,"x":0.1,"y":0.5},
              {"id":"goal","kind":"junction","building":"ГК","floor":1,"x":0.9,"y":0.5},
              {"id":"corridor","kind":"junction","building":"ГК","floor":1,"x":0.5,"y":0.1},
              {"id":"classroom","kind":"room","building":"ГК","floor":1,"x":0.5,"y":0.5,"room":"101"}
            ],"edges":[
              {"from":"start","to":"goal","kind":"walk","seconds":1,"oneWay":false,"points":[[0.1,0.5],[0.5,0.5],[0.9,0.5]]},
              {"from":"start","to":"corridor","kind":"walk","seconds":5,"oneWay":false},
              {"from":"corridor","to":"goal","kind":"walk","seconds":5,"oneWay":false},
              {"from":"corridor","to":"classroom","kind":"walk","seconds":1,"oneWay":false,"points":[[0.5,0.1],[0.5,0.5]]}
            ],"blocked":[
              {"building":"ГК","floor":1,"left":0.4,"top":0.4,"right":0.6,"bottom":0.6,"owner":"classroom"}
            ]}
            """);
        var keepOut = Assert.Single(graph.Blocked!);
        Assert.Equal(("ГК", 1, .4, .4, .6, .6, "classroom"),
            (keepOut.Building, keepOut.Floor, keepOut.Left, keepOut.Top, keepOut.Right, keepOut.Bottom, keepOut.OwnerId));
        Assert.Equal(10, CampusRouter.Find(graph, "start", "goal").Route!.Seconds);
        Assert.True(CampusRouter.Find(graph, "corridor", "classroom").Ok);
    }

    [Fact]
    public void Load_accepts_same_floor_walk()
    {
        var graph = CampusGraph.Load("""
            {"version":1,"buildings":["УЛК"],"nodes":[
              {"id":"a","kind":"junction","building":"УЛК","floor":1,"x":0.1,"y":0.1},
              {"id":"b","kind":"room","building":"УЛК","floor":1,"x":0.2,"y":0.2,"room":"320","label":"320"}
            ],"edges":[{"from":"a","to":"b","kind":"walk","seconds":10,"oneWay":false}]}
            """);
        Assert.Equal(2, graph.Nodes.Count);
        Assert.Equal("junction", graph.Nodes[0].Kind);
        Assert.Equal("320", graph.Nodes[1].Room);
        Assert.Equal("walk", graph.Edges[0].Kind);
        Assert.False(graph.Edges[0].OneWay);
        Assert.Equal(10, graph.Edges[0].Seconds);
    }

    [Fact]
    public void Load_accepts_adjacent_stair_same_group()
    {
        var graph = CampusGraph.Load("""
            {"version":1,"buildings":["УЛК"],"nodes":[
              {"id":"s1","kind":"stair","building":"УЛК","floor":1,"x":0.2,"y":0.2,"group":"s"},
              {"id":"l2","kind":"landing","building":"УЛК","floor":2,"x":0.2,"y":0.2,"group":"s"}
            ],"edges":[{"from":"s1","to":"l2","kind":"stair_up","seconds":20,"oneWay":false}]}
            """);
        Assert.Equal("stair_up", graph.Edges[0].Kind);
        Assert.Equal("s", graph.Nodes[0].Group);
        Assert.Equal("landing", graph.Nodes[1].Kind);
    }

    [Fact]
    public void Load_accepts_building_link_between_link_nodes()
    {
        var graph = CampusGraph.Load("""
            {"version":1,"buildings":["ГК","УЛК"],"nodes":[
              {"id":"g","kind":"building_link","building":"ГК","floor":1,"x":0.9,"y":0.5},
              {"id":"u","kind":"building_link","building":"УЛК","floor":1,"x":0.1,"y":0.5}
            ],"edges":[{"from":"g","to":"u","kind":"building_link","seconds":15,"oneWay":false}]}
            """);
        Assert.Equal("building_link", graph.Edges[0].Kind);
        Assert.Equal("ГК", graph.Nodes[0].Building);
        Assert.Equal("УЛК", graph.Nodes[1].Building);
    }

    [Fact]
    public void Unknown_node_rejects_missing_endpoint()
    {
        var json = """
            {"version":1,"buildings":["УЛК"],"nodes":[
              {"id":"a","kind":"junction","building":"УЛК","floor":1,"x":0.1,"y":0.1}
            ],"edges":[{"from":"a","to":"missing","kind":"walk","seconds":10,"oneWay":false}]}
            """;
        var ex = Assert.Throws<CampusGraphException>(() => CampusGraph.Load(json));
        Assert.Equal("unknown_node", ex.Code);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{"version":2,"buildings":[],"nodes":[],"edges":[]}""")]
    [InlineData("""{"buildings":[],"nodes":[],"edges":[]}""")]
    [InlineData("""{"version":1,"nodes":[],"edges":[]}""")]
    [InlineData("""{"version":1,"buildings":["УЛК"],"edges":[]}""")]
    [InlineData("""{"version":1,"buildings":["УЛК"],"nodes":[]}""")]
    [InlineData("""{"version":1,"buildings":["УЛК"],"nodes":[{"kind":"junction","building":"УЛК","floor":1,"x":0.1,"y":0.1}],"edges":[]}""")]
    [InlineData("""{"version":1,"buildings":["УЛК"],"nodes":[{"id":"a","kind":"junction","building":"УЛК","floor":1,"x":1.1,"y":0.1}],"edges":[]}""")]
    [InlineData("""{"version":1,"buildings":["УЛК"],"nodes":[{"id":"a","kind":"junction","building":"УЛК","floor":1,"x":0.1,"y":-0.01}],"edges":[]}""")]
    [InlineData("""{"version":1,"buildings":["УЛК"],"nodes":[{"id":"a","kind":"junction","building":"УЛК","floor":0,"x":0.1,"y":0.1}],"edges":[]}""")]
    [InlineData("""{"version":1,"buildings":["УЛК"],"nodes":[{"id":"a","kind":"junction","building":"УЛК","floor":6,"x":0.1,"y":0.1}],"edges":[]}""")]
    [InlineData("""{"version":1,"buildings":["УЛК"],"nodes":[{"id":"a","kind":"junction","building":"УЛК","floor":1,"x":0,"y":0}],"edges":[{"from":"a","to":"a","kind":"walk","seconds":0,"oneWay":false}]}""")]
    [InlineData("""{"version":1,"buildings":["УЛК"],"nodes":[{"id":"a","kind":"junction","building":"УЛК","floor":1,"x":0,"y":1}],"edges":[{"from":"a","to":"a","kind":"walk","seconds":-1,"oneWay":false}]}""")]
    [InlineData("""{"version":1,"buildings":["ВЦ"],"nodes":[],"edges":[]}""")]
    [InlineData("""{"version":1,"buildings":["УЛК"],"nodes":[{"id":"a","kind":"portal","building":"УЛК","floor":1,"x":0.1,"y":0.1}],"edges":[]}""")]
    public void Invalid_json_rejects_schema_and_range_errors(string json)
    {
        var ex = Assert.Throws<CampusGraphException>(() => CampusGraph.Load(json));
        Assert.Equal("invalid_json", ex.Code);
    }

    [Fact]
    public void Walk_edge_rejects_different_buildings()
    {
        var json = """
            {"version":1,"buildings":["ГК","УЛК"],"nodes":[
              {"id":"a","kind":"junction","building":"ГК","floor":1,"x":0.1,"y":0.1},
              {"id":"b","kind":"junction","building":"УЛК","floor":1,"x":0.1,"y":0.1}
            ],"edges":[{"from":"a","to":"b","kind":"walk","seconds":10,"oneWay":false}]}
            """;
        var ex = Assert.Throws<CampusGraphException>(() => CampusGraph.Load(json));
        Assert.Equal("bad_edge_floor", ex.Code);
    }

    [Fact]
    public void Stair_rejects_different_group()
    {
        var json = """
            {"version":1,"buildings":["УЛК"],"nodes":[
              {"id":"s1","kind":"stair","building":"УЛК","floor":1,"x":0.2,"y":0.2,"group":"s"},
              {"id":"s2","kind":"stair","building":"УЛК","floor":2,"x":0.2,"y":0.2,"group":"t"}
            ],"edges":[{"from":"s1","to":"s2","kind":"stair_up","seconds":20,"oneWay":false}]}
            """;
        var ex = Assert.Throws<CampusGraphException>(() => CampusGraph.Load(json));
        Assert.Equal("bad_stair", ex.Code);
    }

    [Fact]
    public void Stair_rejects_non_stair_or_landing_endpoint()
    {
        var json = """
            {"version":1,"buildings":["УЛК"],"nodes":[
              {"id":"s1","kind":"stair","building":"УЛК","floor":1,"x":0.2,"y":0.2,"group":"s"},
              {"id":"j2","kind":"junction","building":"УЛК","floor":2,"x":0.2,"y":0.2}
            ],"edges":[{"from":"s1","to":"j2","kind":"stair_up","seconds":20,"oneWay":false}]}
            """;
        var ex = Assert.Throws<CampusGraphException>(() => CampusGraph.Load(json));
        Assert.Equal("bad_stair", ex.Code);
    }

    [Fact]
    public void Building_link_rejects_non_link_endpoints()
    {
        var json = """
            {"version":1,"buildings":["ГК","УЛК"],"nodes":[
              {"id":"a","kind":"junction","building":"ГК","floor":1,"x":0.9,"y":0.5},
              {"id":"b","kind":"building_link","building":"УЛК","floor":1,"x":0.1,"y":0.5}
            ],"edges":[{"from":"a","to":"b","kind":"building_link","seconds":15,"oneWay":false}]}
            """;
        var ex = Assert.Throws<CampusGraphException>(() => CampusGraph.Load(json));
        Assert.Equal("invalid_json", ex.Code);
    }
}
