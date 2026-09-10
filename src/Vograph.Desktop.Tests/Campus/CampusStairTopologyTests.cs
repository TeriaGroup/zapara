using Vograph.Core.Campus;
using Xunit;

namespace Vograph.Desktop.Tests;

public class CampusStairTopologyTests
{
    private static CampusGraph Bundled() => CampusGraph.Load(File.ReadAllText(Path.Combine(
        ResourceKeysTests.RepoRoot(), "src", "Vograph.Desktop", "Assets", "maps", "campus-graph.json")));

    // Corresponding wells identified by rooms/landmarks on the nine source plans,
    // rather than by compass-name equality or nearest normalized coordinates.
    [Theory]
    [InlineData("gk.stair.center-113.2", "gk.stair.center-west.3")]
    [InlineData("gk.stair.bk1.2", "gk.stair.center-east.3")]
    [InlineData("gk.stair.tk.2", "gk.stair.east-north.3")]
    [InlineData("gk.stair.iksh-inner.2", "gk.stair.east-arm.3")]
    [InlineData("gk.stair.sk31.2", "gk.stair.south-mid.3")]
    [InlineData("gk.stair.sk21-north.2", "gk.stair.east-south.3")]
    [InlineData("gk.stair.sk21-south.2", "gk.stair.east-bar.3")]
    [InlineData("gk.stair.west-lab208.2", "gk.stair.west-ne.3")]
    [InlineData("ulk.stair.swtip.1", "ulk.stair.sw.2")]
    public void Corresponding_stair_landings_connect_directly_in_both_directions(string lower, string upper)
    {
        var graph = Bundled();
        var a = graph.Nodes.Single(n => n.Id == lower);
        var b = graph.Nodes.Single(n => n.Id == upper);
        var up = CampusRouter.Find(graph, lower, upper);
        var down = CampusRouter.Find(graph, upper, lower);
        Assert.True(up.Ok, up.Failure);
        Assert.True(down.Ok, down.Failure);
        var ascent = Assert.Single(up.Route!.Legs);
        var descent = Assert.Single(down.Route!.Legs);
        Assert.Equal("stair_up", ascent.Kind);
        Assert.Equal("stair_down", descent.Kind);
        Assert.Equal(new[] { new GraphPoint(a.X, a.Y), new GraphPoint(b.X, b.Y) }, ascent.Points);
        Assert.Equal(new[] { new GraphPoint(b.X, b.Y), new GraphPoint(a.X, a.Y) }, descent.Points);
    }

    [Fact]
    public void Unconfirmed_475_well_cannot_teleport_between_floors()
    {
        var graph = Bundled();
        Assert.DoesNotContain(graph.Edges, edge => edge.Kind.StartsWith("stair_", StringComparison.Ordinal)
            && (edge.From == "gk.stair.475.3" || edge.To == "gk.stair.475.3"));
        Assert.True(CampusRouter.Find(graph, "gk.room.475", "gk.room.474").Ok);
    }

    [Fact]
    public void Vc_stair_marker_is_on_the_well_not_on_its_annotation()
    {
        var stair = Bundled().Nodes.Single(n => n.Id == "gk.stair.west-vc.3");
        Assert.InRange(stair.X * 2001, 718, 742);
        Assert.InRange(stair.Y * 951, 534, 556);
    }
}
