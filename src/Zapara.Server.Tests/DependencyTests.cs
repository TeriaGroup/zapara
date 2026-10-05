using System.Text.Json;
using Xunit;

namespace Zapara.Server.Tests;

public class DependencyTests
{
    [Theory]
    [InlineData("Zapara.Server")]
    [InlineData("Zapara.Ingest")]
    public void Host_dependency_graph_excludes_native_storage(string assembly)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, assembly + ".deps.json")));
        var dependencies = document.RootElement.GetProperty("libraries").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Contains(dependencies, name => name.StartsWith("Vograph.Timetable/", StringComparison.Ordinal));
        Assert.DoesNotContain(dependencies, name => name.StartsWith("Vograph.Core/", StringComparison.Ordinal)
            || name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            || name.Contains("SQLitePCLRaw", StringComparison.OrdinalIgnoreCase));
    }
}
