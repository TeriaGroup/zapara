using Vograph.Core.Campus;
using Xunit;

namespace Vograph.Desktop.Tests;

public class CampusTraceGeometryTests
{
    private static CampusGraph Bundled() => CampusGraph.Load(File.ReadAllText(Path.Combine(
        ResourceKeysTests.RepoRoot(), "src", "Vograph.Desktop", "Assets", "maps", "campus-graph.json")));

    [Fact]
    public void Bundled_graph_blocks_room_interiors_and_doorless_volumes()
    {
        var graph = Bundled();
        Assert.True(graph.Blocked is { Count: >= 300 }, $"blocked={graph.Blocked?.Count}");
        Assert.Contains(graph.Blocked!, b => b.OwnerId == "gk.room.101");
        Assert.Contains(graph.Blocked!, b => b.OwnerId == "ulk.room.564");
        Assert.Contains(graph.Blocked!, b => b.OwnerId is null && b.Building == "УЛК" && b.Floor == 2);
        Assert.Contains(graph.Blocked!, b => b.OwnerId is null && b.Building == "ГК" && b.Floor == 4);
    }

    [Fact]
    public void Ulk_sw_unnamed_rooms_are_an_obstacle_not_a_corridor_chord()
    {
        var graph = Bundled();
        var edge = Assert.Single(graph.Edges, e => e.From == "ulk.j.1.sw0" && e.To == "ulk.j.1.sto0");
        Assert.True(edge.Points is { Count: >= 3 }, "SW wing still uses a two-point chord through unnamed rooms");
        var keep = Assert.Single(graph.Blocked!, b =>
            b.OwnerId is null && b.Building == "УЛК" && b.Floor == 1
            && b.Left < .20 && b.Right > .17 && b.Top < .68 && b.Bottom > .65);
        AssertAvoids(edge.Points!, keep.Left, keep.Top, keep.Right, keep.Bottom, "sw0 → sto0");
        Assert.True(CampusRouter.Find(graph, "ulk.j.1.sw0", "ulk.j.1.sto0").Ok);
        Assert.True(CampusRouter.Find(graph, "ulk.entrance.main", "ulk.j.1.sw0").Ok);
    }

    [Fact]
    public void Ulk_main_to_564_climbs_east_stairs_not_lobby_mid()
    {
        var graph = Bundled();
        var to564 = CampusRouter.Find(graph, "ulk.entrance.main", "ulk.room.564");
        Assert.True(to564.Ok, to564.Failure);
        var stairs = to564.Route!.Legs.Where(l => l.Kind is "stair_up" or "stair_down")
            .Select(leg => graph.Nodes.First(n =>
                n.Kind == "stair" && n.Building == leg.Building && n.Floor == leg.Floor
                && n.X == leg.Points[0].X && n.Y == leg.Points[0].Y).Group)
            .Distinct()
            .ToList();
        Assert.Equal(["ulk.stair.east"], stairs);
        Assert.True(CampusRouter.Find(graph, "ulk.entrance.main", "ulk.stair.mid.1").Ok);
        Assert.True(CampusRouter.Find(graph, "ulk.entrance.main", "ulk.stair.east.1").Ok);
    }

    [Fact]
    public void Bundled_route_to_564_does_not_cut_keep_outs()
    {
        var graph = Bundled();
        var result = CampusRouter.Find(graph, "ulk.entrance.main", "ulk.room.564");
        Assert.True(result.Ok, result.Failure);
        foreach (var leg in result.Route!.Legs.Where(l => l.Kind == "walk"))
        {
            foreach (var region in graph.Blocked!)
            {
                if (region.Building != leg.Building || region.Floor != leg.Floor) continue;
                if (region.OwnerId is "ulk.entrance.main" or "ulk.room.564") continue;
                AssertAvoids(leg.Points, region.Left, region.Top, region.Right, region.Bottom,
                    $"564 route vs {region.OwnerId ?? "unlabeled"}");
            }
        }
    }

    [Fact]
    public void Every_numbered_classroom_on_all_nine_plans_has_a_return_route_to_an_entrance()
    {
        var graph = Bundled();
        var rooms = graph.Nodes.Where(n => n.Kind == "room" && n.Room?.Any(char.IsDigit) == true).ToList();
        Assert.Equal(9, rooms.Select(n => (n.Building, n.Floor)).Distinct().Count());
        foreach (var room in rooms)
        {
            var entrances = graph.Nodes.Where(n => n.Kind == "entrance" && n.Building == room.Building);
            Assert.True(entrances.Any(e => CampusRouter.Find(graph, e.Id, room.Id).Ok
                && CampusRouter.Find(graph, room.Id, e.Id).Ok), $"No entrance/return route: {room.Id}");
        }
    }

    [Fact]
    public void Authored_walk_polylines_join_their_graph_nodes_without_gaps()
    {
        var graph = Bundled();
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        foreach (var edge in graph.Edges.Where(e => e.Kind == "walk" && e.Points is { Count: > 0 }))
        {
            var a = nodes[edge.From];
            var b = nodes[edge.To];
            Assert.Equal(new GraphPoint(a.X, a.Y), edge.Points![0]);
            Assert.Equal(new GraphPoint(b.X, b.Y), edge.Points[^1]);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Ulk_centre_wing_access_does_not_cut_across_the_open_courtyard(int floor)
    {
        var graph = Bundled();
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        foreach (var edge in graph.Edges.Where(e => e.Kind == "walk"
            && nodes[e.From].Building == "УЛК" && nodes[e.From].Floor == floor))
        {
            var a = nodes[edge.From];
            var b = nodes[edge.To];
            AssertAvoids(edge.Points ?? [new(a.X, a.Y), new(b.X, b.Y)],
                .785, .275, .810, .305, $"{edge.From} → {edge.To}");
        }
    }

    [Fact]
    public void Adjacent_ulk_east_wing_rooms_use_the_corridor_between_their_doors()
    {
        var route = CampusRouter.Find(Bundled(), "ulk.room.251", "ulk.room.253").Route;
        Assert.NotNull(route);
        Assert.All(route!.Legs, leg => Assert.Equal("walk", leg.Kind));
        var distance = route.Legs.Sum(leg => leg.Points.Zip(leg.Points.Skip(1))
            .Sum(pair => Math.Sqrt(Math.Pow(pair.First.X - pair.Second.X, 2) + Math.Pow(pair.First.Y - pair.Second.Y, 2))));
        Assert.True(distance < .11, $"Adjacent rooms retrace to a distant junction: {distance:F4}");
    }

    [Fact]
    public void Gk_cloakroom_access_does_not_use_classroom_101_as_a_corridor()
    {
        var graph = Bundled();
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        foreach (var edge in graph.Edges.Where(e => e.Kind == "walk"
            && nodes[e.From].Building == "ГК" && nodes[e.From].Floor == 1
            && e.From != "gk.room.101" && e.To != "gk.room.101"))
        {
            var a = nodes[edge.From];
            var b = nodes[edge.To];
            AssertAvoids(edge.Points ?? [new(a.X, a.Y), new(b.X, b.Y)],
                995.0 / 2001, 819.0 / 951, 1042.0 / 2001, 855.0 / 951, $"{edge.From} → {edge.To}");
        }
    }

    // Keep-out regions read from the original 2001×951 floor plans, not from the graph.
    [Theory]
    [InlineData("gk.j.4.sports.aw", "gk.j.4.sports.ae", .485, .80, .63, .885)]
    [InlineData("gk.j.2.south-w", "gk.j.2.r211", .45, .861, .545, .90)]
    public void Corridor_routes_avoid_the_act_hall_and_south_classrooms(
        string from, string to, double left, double top, double right, double bottom)
    {
        var result = CampusRouter.Find(Bundled(), from, to);
        Assert.True(result.Ok, result.Failure);
        Assert.All(result.Route!.Legs, leg => Assert.Equal("walk", leg.Kind));
        foreach (var leg in result.Route.Legs)
            AssertAvoids(leg.Points, left, top, right, bottom, $"{from} → {to}");
    }

    private static void AssertAvoids(IReadOnlyList<GraphPoint> points,
        double left, double top, double right, double bottom, string description)
    {
        foreach (var (a, b) in points.Zip(points.Skip(1)))
        {
            // Dense sampling also catches chords whose endpoints lie outside the room rectangle.
            for (var i = 0; i <= 100; i++)
            {
                var x = a.X + (b.X - a.X) * i / 100;
                var y = a.Y + (b.Y - a.Y) * i / 100;
                Assert.False(x > left && x < right && y > top && y < bottom,
                    $"{description} crosses a blocked area at ({x:F4}, {y:F4})");
            }
        }
    }
}
