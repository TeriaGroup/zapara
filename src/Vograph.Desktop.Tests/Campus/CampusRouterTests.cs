using Vograph.Core.Campus;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class CampusRouterTests
{
    [Fact]
    public void Exact_node_id_wins_over_a_room_label()
    {
        var graph = new CampusGraph(1, ["ГК"], [
            new("room", "room", "ГК", 1, .1, .1, Room: "target"),
            new("target", "junction", "ГК", 1, .2, .1)
        ], []);

        Assert.Equal("target", CampusRouter.Resolve(graph, "target")?.Id);
    }

    [Fact]
    public void Ambiguous_generic_room_label_is_unresolved()
    {
        var graph = new CampusGraph(1, ["ГК", "УЛК"], [
            new("a", "room", "ГК", 1, .1, .1, Room: "101"),
            new("b", "room", "УЛК", 1, .2, .1, Room: "101")
        ], []);

        Assert.Null(CampusRouter.Resolve(graph, "101"));
        Assert.Equal("unknown_place", CampusRouter.FindResolved(graph, "101", "a").Failure);
        Assert.Equal("b", CampusRouter.Resolve(graph, "b")?.Id);
    }

    [Fact]
    public void Walk_chord_through_a_doorless_box_is_skipped_when_a_corridor_exists()
    {
        var graph = new CampusGraph(1, ["ГК"], [
            new("start", "junction", "ГК", 1, .1, .5),
            new("goal", "junction", "ГК", 1, .9, .5),
            new("corridor", "junction", "ГК", 1, .5, .1)
        ], [
            new("start", "goal", "walk", 1, false, [new(.1, .5), new(.5, .5), new(.9, .5)]),
            new("start", "corridor", "walk", 5, false),
            new("corridor", "goal", "walk", 5, false)
        ], [new BlockedRegion("ГК", 1, .4, .4, .6, .6)]);

        var route = CampusRouter.Find(graph, "start", "goal").Route!;
        Assert.Equal(10, route.Seconds);
        Assert.DoesNotContain(route.Legs, leg =>
            leg.Points.Any(p => p.X > .4 && p.X < .6 && p.Y > .4 && p.Y < .6));
    }

    [Fact]
    public void Approach_into_a_room_may_enter_that_room_keep_out()
    {
        var graph = new CampusGraph(1, ["ГК"], [
            new("door", "junction", "ГК", 1, .1, .5),
            new("classroom", "room", "ГК", 1, .5, .5, Room: "101")
        ], [
            new("door", "classroom", "walk", 1, false, [new(.1, .5), new(.5, .5)])
        ], [new BlockedRegion("ГК", 1, .4, .4, .6, .6, OwnerId: "classroom")]);

        Assert.True(CampusRouter.Find(graph, "door", "classroom").Ok);
    }

    [Fact]
    public void Unlabeled_keep_out_blocks_an_edge_that_ends_inside_it()
    {
        var graph = new CampusGraph(1, ["ГК"], [
            new("door", "junction", "ГК", 1, .1, .5),
            new("inside", "junction", "ГК", 1, .5, .5)
        ], [new("door", "inside", "walk", 1, false)],
            [new BlockedRegion("ГК", 1, .4, .4, .6, .6)]);

        Assert.Equal("unreachable", CampusRouter.Find(graph, "door", "inside").Failure);
    }

    [Fact]
    public void Room_nodes_are_endpoints_and_never_corridor_shortcuts()
    {
        var graph = RoomShortcutGraph();

        Assert.Equal(10, CampusRouter.Find(graph, "start", "goal").Route!.Seconds);
        Assert.Equal(1, CampusRouter.Find(graph, "classroom", "goal").Route!.Seconds);
        Assert.Equal(1, CampusRouter.Find(graph, "start", "classroom").Route!.Seconds);
    }

    [Fact]
    public void Route_is_unreachable_when_it_would_require_crossing_an_intermediate_room()
    {
        var graph = RoomShortcutGraph();
        graph = graph with { Edges = graph.Edges.Take(2).ToArray() };

        Assert.Equal("unreachable", CampusRouter.Find(graph, "start", "goal").Failure);
    }

    private static CampusGraph RoomShortcutGraph() => new(1, ["ГК"], [
        new("start", "junction", "ГК", 1, .1, .1), new("goal", "junction", "ГК", 1, .9, .1),
        new("classroom", "room", "ГК", 1, .5, .1, Room: "101"), new("corridor", "junction", "ГК", 1, .5, .5)
    ], [new("start", "classroom", "walk", 1, false), new("classroom", "goal", "walk", 1, false),
        new("start", "corridor", "walk", 5, false), new("corridor", "goal", "walk", 5, false)]);

    [Fact]
    public void Floor_changing_links_do_not_hide_a_faster_route()
    {
        var graph = new CampusGraph(1, ["ГК", "УЛК"], [
            new("start", "building_link", "ГК", 2, .1, .1),
            new("goal", "building_link", "ГК", 2, .9, .1),
            new("bridge", "building_link", "УЛК", 1, .5, .5),
            new("stair1", "stair", "ГК", 1, .1, .1, Group: "shaft"),
            new("stair2", "stair", "ГК", 2, .1, .1, Group: "shaft")
        ], [
            new("start", "goal", "walk", 50, false),
            new("start", "bridge", "building_link", 1, false),
            new("bridge", "goal", "building_link", 1, false),
            new("stair1", "stair2", "stair_up", 100, false)
        ]);

        var route = CampusRouter.Find(graph, "start", "goal").Route!;
        Assert.Equal(2, route.Seconds);
        Assert.Equal(2, route.Legs.Count);
        Assert.All(route.Legs, leg => Assert.Equal("building_link", leg.Kind));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equal_time_routes_choose_fewer_stairs_in_both_directions(bool reverse)
    {
        var route = CampusRouter.Find(StairChoiceGraph(), reverse ? "goal" : "start", reverse ? "start" : "goal").Route!;

        Assert.Equal(3, route.Seconds);
        Assert.All(route.Legs, leg => Assert.Equal("walk", leg.Kind));
    }

    [Fact]
    public void Faster_stair_route_wins_over_a_slower_corridor_route()
    {
        var graph = StairChoiceGraph();
        graph = graph with { Edges = graph.Edges.Select(edge =>
            edge.From == "corridor" || edge.To == "corridor" ? edge with { Seconds = 10 } : edge).ToArray() };

        var route = CampusRouter.Find(graph, "start", "goal").Route!;
        Assert.Equal(3, route.Seconds);
        Assert.Equal(new[] { "stair_up", "walk", "stair_down" }, route.Legs.Select(leg => leg.Kind));
    }

    [Fact]
    public void Equal_time_routes_are_stable_when_input_order_changes()
    {
        var graph = new CampusGraph(1, ["ГК"], [
            new("start", "junction", "ГК", 1, .1, .5),
            new("a", "junction", "ГК", 1, .2, .1),
            new("b", "junction", "ГК", 1, .2, .9),
            new("goal", "junction", "ГК", 1, .9, .5)
        ], [new("start", "b", "walk", 1, false), new("b", "goal", "walk", 1, false),
            new("start", "a", "walk", 1, false), new("a", "goal", "walk", 1, false)]);

        foreach (var ordered in new[] { graph, graph with { Nodes = graph.Nodes.Reverse().ToArray(), Edges = graph.Edges.Reverse().ToArray() } })
        {
            var route = CampusRouter.Find(ordered, "start", "goal").Route!;
            Assert.Equal(new GraphPoint(.2, .1), route.Legs[0].Points[^1]);
        }
    }

    [Fact]
    public void Reversed_stair_respects_one_way_and_reverses_direction_and_geometry()
    {
        var down = new Edge("upper", "lower", "stair_down", 20, true,
            [new(.2, .2), new(.3, .4), new(.7, .8)]);
        var graph = new CampusGraph(1, ["ГК"], [
            new("upper", "stair", "ГК", 2, .2, .2, Group: "s"),
            new("lower", "stair", "ГК", 1, .7, .8, Group: "s")
        ], [down]);

        Assert.Equal("unreachable", CampusRouter.Find(graph, "lower", "upper").Failure);
        var route = CampusRouter.Find(graph with { Edges = [down with { OneWay = false }] }, "lower", "upper").Route!;
        var leg = Assert.Single(route.Legs);
        Assert.Equal(("stair_up", 1, 2), (leg.Kind, leg.Floor, leg.ToFloor));
        Assert.Equal(new[] { new GraphPoint(.7, .8), new GraphPoint(.3, .4), new GraphPoint(.2, .2) }, leg.Points);
    }

    private static CampusGraph StairChoiceGraph() => new(1, ["ГК"], [
        new("start", "stair", "ГК", 1, .1, .1, Group: "a"),
        new("up", "stair", "ГК", 2, .1, .1, Group: "a"),
        new("across", "stair", "ГК", 2, .9, .1, Group: "b"),
        new("goal", "stair", "ГК", 1, .9, .1, Group: "b"),
        new("corridor", "junction", "ГК", 1, .5, .1),
        new("cheap1", "stair", "ГК", 1, .5, .5, Group: "cheap"),
        new("cheap2", "stair", "ГК", 2, .5, .5, Group: "cheap")
    ], [new("start", "up", "stair_up", 1, false), new("up", "across", "walk", 1, false),
        new("across", "goal", "stair_down", 1, false), new("start", "corridor", "walk", 2.5, false),
        new("corridor", "goal", "walk", .5, false), new("cheap1", "cheap2", "stair_up", .1, false)]);

    [Fact]
    public void Consecutive_walks_on_one_floor_produce_one_instruction_without_changing_geometry()
    {
        var legs = new[] { Walk("УЛК", 3), Walk("УЛК", 3), Walk("УЛК", 3) };
        var route = new Route(30, legs, []);

        Assert.Equal(new[] { new I18nService().T("routeWalk", 3) }, RouteSteps.Format(route, new()));
        Assert.Same(legs, route.Legs);
        Assert.Equal(3, route.Legs.Count);
        Assert.All(route.Legs, leg => Assert.Equal(2, leg.Points.Count));
    }

    [Fact]
    public void Stairs_break_walk_instruction_runs_even_when_the_route_returns_to_the_same_floor()
    {
        var route = new Route(80, [
            Walk("УЛК", 3), Walk("УЛК", 3),
            new Leg("stair_down", "УЛК", 3, "УЛК", 2, []),
            new Leg("stair_up", "УЛК", 2, "УЛК", 3, []),
            Walk("УЛК", 3), Walk("УЛК", 3)
        ], []);
        var copy = new I18nService();

        Assert.Equal(new[] { copy.T("routeWalk", 3), copy.T("routeStairDown", 2),
            copy.T("routeStairUp", 3), copy.T("routeWalk", 3) }, RouteSteps.Format(route, copy));
    }

    [Fact]
    public void Walks_in_different_buildings_or_floors_remain_separate_instructions()
    {
        var route = new Route(30, [Walk("УЛК", 3), Walk("ГК", 3), Walk("ГК", 4)], []);

        Assert.Equal(3, RouteSteps.Format(route, new()).Count);
    }

    [Fact]
    public void Building_links_break_walk_instruction_runs()
    {
        var route = new Route(30, [
            Walk("УЛК", 1), Walk("УЛК", 1),
            new Leg("building_link", "УЛК", 1, "ГК", 1, []),
            Walk("ГК", 1), Walk("ГК", 1)
        ], []);
        var copy = new I18nService();

        Assert.Equal(new[] { copy.T("routeWalk", 1), copy.T("routeLink", "ГК", 1),
            copy.T("routeWalk", 1) }, RouteSteps.Format(route, copy));
    }

    private static Leg Walk(string building, int floor) =>
        new("walk", building, floor, null, null, [new GraphPoint(.1, .2), new GraphPoint(.2, .3)]);

    [Fact]
    public void Fractional_route_duration_rounds_half_seconds_up_on_both_platforms()
    {
        var graph = new CampusGraph(1, ["УЛК"], [
            new Node("a", "room", "УЛК", 1, 0.1, 0.1),
            new Node("b", "room", "УЛК", 1, 0.2, 0.1)
        ], [new Edge("a", "b", "walk", 2.5, false)]);

        Assert.Equal(3, CampusRouter.Find(graph, "a", "b").Route!.Seconds);
    }

    [Fact]
    public void Must_descend_to_reach_other_wing_on_same_floor()
    {
        var g = SyntheticLabyrinth.Build();
        var route = CampusRouter.Find(g, "lab.room.west.3", "lab.room.east.3").Route;
        Assert.NotNull(route);
        Assert.Contains(route!.Legs, l => l.Kind is "stair_down");
        Assert.Contains(route.Legs, l => l.Kind is "stair_up");
        Assert.DoesNotContain(route.Legs, l => l.Kind == "walk" && l.Floor == 3 && l.Points.Count > 2);
    }

    [Fact]
    public void Path_includes_floor_1_walk_and_not_floor_2_walk()
    {
        var route = CampusRouter.Find(SyntheticLabyrinth.Build(), "lab.room.west.3", "lab.room.east.3").Route;
        Assert.NotNull(route);
        Assert.Contains(route!.Legs, l => l.Kind == "walk" && l.Floor == 1);
        Assert.DoesNotContain(route.Legs, l => l.Kind == "walk" && l.Floor == 2);
    }

    [Theory]
    [InlineData("lab.no.such", "lab.room.east.3")]
    [InlineData("lab.room.west.3", "lab.no.such")]
    public void Unknown_place_when_node_id_missing(string fromId, string toId)
    {
        var result = CampusRouter.Find(SyntheticLabyrinth.Build(), fromId, toId);
        Assert.False(result.Ok);
        Assert.Null(result.Route);
        Assert.Equal("unknown_place", result.Failure);
    }

    [Fact]
    public void Unreachable_when_nodes_exist_but_disconnected()
    {
        var g = CampusGraph.Load("""
            {"version":1,"buildings":["УЛК"],"nodes":[
              {"id":"a","kind":"room","building":"УЛК","floor":1,"x":0.1,"y":0.1},
              {"id":"b","kind":"room","building":"УЛК","floor":1,"x":0.9,"y":0.9}
            ],"edges":[]}
            """);
        var result = CampusRouter.Find(g, "a", "b");
        Assert.False(result.Ok);
        Assert.Null(result.Route);
        Assert.Equal("unreachable", result.Failure);
    }

    [Fact]
    public void Reverse_of_bidirectional_stair_up_is_stair_down_leg()
    {
        var g = CampusGraph.Load("""
            {"version":1,"buildings":["УЛК"],"nodes":[
              {"id":"s1","kind":"stair","building":"УЛК","floor":1,"x":0.2,"y":0.2,"group":"s"},
              {"id":"s2","kind":"landing","building":"УЛК","floor":2,"x":0.2,"y":0.2,"group":"s"}
            ],"edges":[{"from":"s1","to":"s2","kind":"stair_up","seconds":20,"oneWay":false}]}
            """);
        var route = CampusRouter.Find(g, "s2", "s1").Route;
        Assert.NotNull(route);
        Assert.Contains(route!.Legs, l => l.Kind == "stair_down");
        Assert.DoesNotContain(route.Legs, l => l.Kind == "stair_up");
    }

    [Fact]
    public void Steps_mention_going_down_then_up()
    {
        var g = SyntheticLabyrinth.Build();
        var route = CampusRouter.Find(g, "lab.room.west.3", "lab.room.east.3").Route!;
        var i18n = new I18nService();
        var steps = RouteSteps.Format(route, i18n);
        Assert.Contains(steps, s => s.Contains("спуск", StringComparison.OrdinalIgnoreCase) || s.Contains("Спуститесь"));
    }

    [Fact]
    public void Steps_mention_ascent()
    {
        var route = CampusRouter.Find(SyntheticLabyrinth.Build(), "lab.room.west.3", "lab.room.east.3").Route!;
        var steps = RouteSteps.Format(route, new I18nService());
        Assert.Contains(steps, s => s.Contains("подъём", StringComparison.OrdinalIgnoreCase) || s.Contains("Поднимитесь"));
    }

    [Fact]
    public void Reverse_stair_up_step_is_descent()
    {
        var g = CampusGraph.Load("""
            {"version":1,"buildings":["УЛК"],"nodes":[
              {"id":"s1","kind":"stair","building":"УЛК","floor":1,"x":0.2,"y":0.2,"group":"s"},
              {"id":"s2","kind":"landing","building":"УЛК","floor":2,"x":0.2,"y":0.2,"group":"s"}
            ],"edges":[{"from":"s1","to":"s2","kind":"stair_up","seconds":20,"oneWay":false}]}
            """);
        var route = CampusRouter.Find(g, "s2", "s1").Route!;
        var steps = RouteSteps.Format(route, new I18nService());
        Assert.Contains(steps, s => s.Contains("Спуститесь"));
        Assert.DoesNotContain(steps, s => s.Contains("Поднимитесь"));
    }

    [Theory]
    [InlineData("routeStairDown")]
    [InlineData("routeStairUp")]
    [InlineData("routeWalk")]
    [InlineData("routeEnter")]
    [InlineData("routeLink")]
    public void Route_step_key_is_russian(string key)
    {
        var value = new I18nService().T(key);
        Assert.NotEqual(key, value);
        Assert.Contains(value, c => c is >= '\u0400' and <= '\u04FF');
    }

    [Fact]
    public void FindFromEntrance_missing_entrance_is_unknown_place()
    {
        var result = CampusRouter.FindFromEntrance(EntranceGraph(), "no.such", "A1");
        Assert.False(result.Ok);
        Assert.Null(result.Route);
        Assert.Equal("unknown_place", result.Failure);
    }

    [Fact]
    public void FindFromEntrance_room_id_as_entrance_is_unknown_place()
    {
        var result = CampusRouter.FindFromEntrance(EntranceGraph(), "lab.room.a", "A1");
        Assert.False(result.Ok);
        Assert.Equal("unknown_place", result.Failure);
    }

    [Fact]
    public void FindFromEntrance_missing_room_is_unknown_place()
    {
        var result = CampusRouter.FindFromEntrance(EntranceGraph(), "lab.entrance", "no.room");
        Assert.False(result.Ok);
        Assert.Equal("unknown_place", result.Failure);
    }

    [Fact]
    public void FindFromEntrance_unreachable_room_is_unreachable()
    {
        var result = CampusRouter.FindFromEntrance(EntranceGraph(), "lab.entrance", "B1");
        Assert.False(result.Ok);
        Assert.Null(result.Route);
        Assert.Equal("unreachable", result.Failure);
    }

    [Fact]
    public void FindFromEntrance_resolves_room_key_and_strips_coords_junk()
    {
        var result = CampusRouter.FindFromEntrance(EntranceGraph(), "lab.entrance", "A1*;");
        Assert.True(result.Ok);
        Assert.NotNull(result.Route);
        Assert.Contains(result.Route!.Legs, l => l.Kind == "walk");
    }

    [Fact]
    public void FindFromEntrance_resolves_room_by_node_id()
    {
        var result = CampusRouter.FindFromEntrance(EntranceGraph(), "lab.entrance", "lab.room.a");
        Assert.True(result.Ok);
        Assert.NotNull(result.Route);
    }

    [Fact]
    public void FindFromEntrance_labyrinth_climbs_from_floor1_entrance()
    {
        var route = CampusRouter.FindFromEntrance(LabyrinthWithEntrance(), "lab.entrance", "E3").Route;
        Assert.NotNull(route);
        Assert.Contains(route!.Legs, l => l.Kind == "stair_up");
        Assert.Contains(route.Legs, l => l.Kind == "walk" && l.Floor == 1);
        Assert.DoesNotContain(route.Legs, l => l.Kind == "walk" && l.Floor == 3 && l.Points.Count > 2);
    }

    [Fact]
    public void Resolve_matches_unique_room_fields_and_node_ids()
    {
        var g = EntranceGraph();
        Assert.Equal("lab.room.a", CampusRouter.Resolve(g, "A1")!.Id);
        Assert.Equal("lab.room.a", CampusRouter.Resolve(g, "lab.room.a")!.Id);
        Assert.Equal("lab.entrance", CampusRouter.Resolve(g, "lab.entrance")!.Id);
        Assert.Null(CampusRouter.Resolve(g, "missing"));
        Assert.Null(CampusRouter.Resolve(g, null));
    }

    [Fact]
    public void Find_between_room_keys_uses_resolve()
    {
        var result = CampusRouter.FindResolved(LabyrinthWithEntrance(), "W3", "E3");
        Assert.True(result.Ok);
        Assert.Contains(result.Route!.Legs, l => l.Kind == "stair_down");
    }

    [Fact]
    public void ChooseFrom_prefers_previous_room_node_else_entrance()
    {
        var g = EntranceGraph();
        Assert.Equal("lab.room.a", CampusRouter.ChooseFrom(g, "A1", "lab.entrance")!.Id);
        Assert.Equal("lab.entrance", CampusRouter.ChooseFrom(g, "no.room", "lab.entrance")!.Id);
        Assert.Null(CampusRouter.ChooseFrom(g, "no.room", "no.entrance"));
        Assert.Empty(CampusRouter.Entrances(CampusGraph.Load("""{"version":1,"buildings":["ГК","УЛК"],"nodes":[],"edges":[]}""")));
        Assert.Equal("lab.entrance", Assert.Single(CampusRouter.Entrances(g)).Id);
    }

    private static CampusGraph EntranceGraph() => CampusGraph.Load("""
        {"version":1,"buildings":["УЛК"],"nodes":[
          {"id":"lab.entrance","kind":"entrance","building":"УЛК","floor":1,"x":0.1,"y":0.9,"label":"Вход лабиринта"},
          {"id":"lab.room.a","kind":"room","building":"УЛК","floor":1,"x":0.2,"y":0.2,"room":"A1"},
          {"id":"lab.room.b","kind":"room","building":"УЛК","floor":1,"x":0.8,"y":0.2,"room":"B1"}
        ],"edges":[{"from":"lab.entrance","to":"lab.room.a","kind":"walk","seconds":10,"oneWay":false}]}
        """);

    private static CampusGraph LabyrinthWithEntrance() => CampusGraph.Load("""
        {
          "version": 1,
          "buildings": ["УЛК"],
          "nodes": [
            {"id":"lab.entrance","kind":"entrance","building":"УЛК","floor":1,"x":0.1,"y":0.9,"label":"Вход лабиринта"},
            {"id":"lab.room.west.3","kind":"room","building":"УЛК","floor":3,"x":0.2,"y":0.2,"room":"W3"},
            {"id":"lab.stair.west.3","kind":"stair","building":"УЛК","floor":3,"x":0.2,"y":0.5,"group":"lab.stair.west"},
            {"id":"lab.stair.west.2","kind":"stair","building":"УЛК","floor":2,"x":0.2,"y":0.5,"group":"lab.stair.west"},
            {"id":"lab.stair.west.1","kind":"stair","building":"УЛК","floor":1,"x":0.2,"y":0.5,"group":"lab.stair.west"},
            {"id":"lab.room.east.3","kind":"room","building":"УЛК","floor":3,"x":0.8,"y":0.2,"room":"E3"},
            {"id":"lab.stair.east.3","kind":"stair","building":"УЛК","floor":3,"x":0.8,"y":0.5,"group":"lab.stair.east"},
            {"id":"lab.stair.east.2","kind":"stair","building":"УЛК","floor":2,"x":0.8,"y":0.5,"group":"lab.stair.east"},
            {"id":"lab.stair.east.1","kind":"stair","building":"УЛК","floor":1,"x":0.8,"y":0.5,"group":"lab.stair.east"}
          ],
          "edges": [
            {"from":"lab.entrance","to":"lab.stair.west.1","kind":"walk","seconds":5,"oneWay":false},
            {"from":"lab.room.west.3","to":"lab.stair.west.3","kind":"walk","seconds":10,"oneWay":false},
            {"from":"lab.room.east.3","to":"lab.stair.east.3","kind":"walk","seconds":10,"oneWay":false},
            {"from":"lab.stair.west.3","to":"lab.stair.west.2","kind":"stair_down","seconds":20,"oneWay":true},
            {"from":"lab.stair.west.2","to":"lab.stair.west.1","kind":"stair_down","seconds":20,"oneWay":true},
            {"from":"lab.stair.west.1","to":"lab.stair.west.2","kind":"stair_up","seconds":20,"oneWay":true},
            {"from":"lab.stair.west.2","to":"lab.stair.west.3","kind":"stair_up","seconds":20,"oneWay":true},
            {"from":"lab.stair.east.3","to":"lab.stair.east.2","kind":"stair_down","seconds":20,"oneWay":true},
            {"from":"lab.stair.east.2","to":"lab.stair.east.1","kind":"stair_down","seconds":20,"oneWay":true},
            {"from":"lab.stair.east.1","to":"lab.stair.east.2","kind":"stair_up","seconds":20,"oneWay":true},
            {"from":"lab.stair.east.2","to":"lab.stair.east.3","kind":"stair_up","seconds":20,"oneWay":true},
            {"from":"lab.stair.west.1","to":"lab.stair.east.1","kind":"walk","seconds":30,"oneWay":false,"points":[[0.2,0.5],[0.5,0.5],[0.8,0.5]]}
          ]
        }
        """);
}
