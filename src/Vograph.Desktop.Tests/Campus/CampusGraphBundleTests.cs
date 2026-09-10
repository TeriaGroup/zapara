using Vograph.Core.Campus;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class CampusGraphBundleTests
{
    [Fact]
    public void Bundled_source_graphs_are_identical_and_load()
    {
        var root = ResourceKeysTests.RepoRoot();
        var paths = new[]
        {
            Path.Combine(root, "src", "Vograph.Desktop", "Assets", "maps", "campus-graph.json"),
            Path.Combine(root, "android", "app", "src", "main", "assets", "maps", "campus-graph.json"),
        };

        Assert.True(File.Exists(paths[0]), paths[0]);
        Assert.True(File.Exists(paths[1]), paths[1]);
        Assert.Equal(File.ReadAllBytes(paths[0]), File.ReadAllBytes(paths[1]));

        foreach (var path in paths)
        {
            var graph = CampusGraph.Load(File.ReadAllText(path));
            Assert.Equal(1, graph.Version);
            Assert.Equal(["ГК", "УЛК"], graph.Buildings);
            Assert.NotEmpty(graph.Nodes);
            Assert.NotEmpty(graph.Edges);
            Assert.Contains(graph.Nodes, n => n.Kind == "entrance");
            Assert.Contains(graph.Nodes, n => n.Kind == "stair");
            Assert.Contains(graph.Edges, e => e.Kind is "stair_up" or "stair_down");
            Assert.True(graph.Blocked is { Count: > 0 });
            Assert.Contains(graph.Blocked!, b => b.OwnerId is null);

            var missing = CampusRouter.Find(graph, "a", "b");
            Assert.False(missing.Ok);
            Assert.Equal("unknown_place", missing.Failure);
        }
    }

    [Fact]
    public void RouteUnmarked_is_the_task_7_copy()
    {
        Assert.Equal("маршрут ещё не размечен", new I18nService().T("routeUnmarked"));
    }
}
