using Vograph.Core.Campus;
using Xunit;

namespace Vograph.Desktop.Tests;

public class CampusAuthoredGraphTests
{
    private static CampusGraph Bundled()
    {
        var path = Path.Combine(ResourceKeysTests.RepoRoot(), "src", "Vograph.Desktop", "Assets", "maps", "campus-graph.json");
        return CampusGraph.Load(File.ReadAllText(path));
    }

    [Fact]
    public void Ulk_entrance_to_320_walks_the_L()
    {
        var route = CampusRouter.FindFromEntrance(Bundled(), "ulk.entrance.main", "320").Route;
        Assert.NotNull(route);
        Assert.Contains(route!.Legs, l => l.Kind == "walk");
        Assert.DoesNotContain(route.Legs, l => l.Kind == "building_link");
    }

    [Fact]
    public void Gk_493_to_401_must_change_floors()
    {
        var g = Bundled();
        var route = CampusRouter.Find(g, "gk.room.493", "gk.room.401").Route;
        Assert.NotNull(route);
        Assert.Contains(route!.Legs, l => l.Kind == "stair_down");
        Assert.Contains(route.Legs, l => l.Kind == "stair_up");
        Assert.DoesNotContain(route.Legs, l => l.Kind == "walk" && l.Floor == 4 && l.Points.Count > 8);
    }

    [Fact]
    public void Ulk_320_to_gk_493_uses_yard_link()
    {
        var route = CampusRouter.Find(Bundled(), "ulk.room.320", "gk.room.493").Route;
        Assert.NotNull(route);
        Assert.Contains(route!.Legs, l => l.Kind == "building_link");
        Assert.Contains(route.Legs, l => l.Kind == "stair_down" || l.Kind == "stair_up");
    }

    [Fact]
    public void Ulk_320_to_325_stays_on_floor_3()
    {
        var route = CampusRouter.Find(Bundled(), "ulk.room.320", "ulk.room.325").Route;
        Assert.NotNull(route);
        Assert.DoesNotContain(route!.Legs, l => l.Kind is "stair_up" or "stair_down");
        Assert.All(route.Legs.Where(l => l.Kind == "walk"), l => Assert.Equal(3, l.Floor));
    }

    [Fact]
    public void Ulk_507_to_320_uses_stairs()
    {
        var route = CampusRouter.Find(Bundled(), "ulk.room.507", "ulk.room.320").Route;
        Assert.NotNull(route);
        Assert.Contains(route!.Legs, l => l.Kind is "stair_down" or "stair_up");
    }

    [Fact]
    public void Gk_main_entrance_to_493_goes_up()
    {
        var route = CampusRouter.FindFromEntrance(Bundled(), "gk.entrance.main", "493").Route;
        Assert.NotNull(route);
        Assert.Contains(route!.Legs, l => l.Kind == "stair_up");
    }
}
