using Vograph.Core.Campus;
using Xunit;

namespace Vograph.Desktop.Tests;

public class CampusClassroomResolutionTests
{
    private static CampusGraph Bundled() => CampusGraph.Load(File.ReadAllText(Path.Combine(
        ResourceKeysTests.RepoRoot(), "src", "Vograph.Desktop", "Assets", "maps", "campus-graph.json")));

    [Theory]
    [InlineData("401;", "gk.room.401")]
    [InlineData(" 401*; ", "ulk.room.401")]
    [InlineData("219А;", "gk.room.219a")]
    [InlineData("219А*;", "ulk.room.219а")]
    [InlineData("ВЦ 280;", "gk.room.280")]
    [InlineData("вц280;", "gk.room.280")]
    [InlineData("ВЦ КЕ1;", "gk.room.vcke1")]
    [InlineData("ulk.room.319", "ulk.room.319")]
    public void Classroom_notation_resolves_to_the_correct_place(string raw, string expectedId)
    {
        Assert.Equal(expectedId, CampusRouter.ResolveClassroom(Bundled(), raw)?.Id);
    }

    [Theory]
    [InlineData("320")]
    [InlineData("493*")]
    [InlineData("219б*")]
    [InlineData("дистанционно")]
    public void Missing_classroom_does_not_fall_back_to_another_building_or_suffix(string raw)
    {
        Assert.Null(CampusRouter.ResolveClassroom(Bundled(), raw));
    }

    [Fact]
    public void Duplicate_room_labels_require_an_explicit_node_id()
    {
        var graph = new CampusGraph(1, ["УЛК"], [
            new Node("west", "room", "УЛК", 3, 0.1, 0.1, Room: "319"),
            new Node("east", "room", "УЛК", 3, 0.9, 0.1, Room: "319")
        ], []);

        Assert.Null(CampusRouter.ResolveClassroom(graph, "319*"));
        Assert.Equal("east", CampusRouter.ResolveClassroom(graph, "east")?.Id);
    }
}
