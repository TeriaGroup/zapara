using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>The offline bundle Core expects next to the exe (and the updater zip ships): nine plans, the room
/// coordinates and the lecturer timetable. Copied transitively into the test output through the project reference.</summary>
public class AssetsTests
{
    [Fact]
    public void Bundled_Assets_Are_Copied_Next_To_The_Binaries()
    {
        var root = AppContext.BaseDirectory;
        var maps = Directory.GetFiles(Path.Combine(root, "maps"), "karta-*.jpg");
        Assert.Equal(9, maps.Length);
        Assert.All(maps, f => Assert.True(new FileInfo(f).Length > 50_000, f));
        Assert.True(File.Exists(Path.Combine(root, "maps", "coords.json")));
        var lecturers = new FileInfo(Path.Combine(root, "TimetableLecturer50.xml"));
        Assert.True(lecturers.Exists);
        Assert.True(lecturers.Length > 1_000_000, "the lecturer timetable is a ~4 MB XML");
    }

    [Fact]
    public void Assets_Live_In_The_Desktop_Project()
    {
        var assets = Path.Combine(ResourceKeysTests.RepoRoot(), "src", "Vograph.Desktop", "Assets");
        Assert.Equal(9, Directory.GetFiles(Path.Combine(assets, "maps"), "karta-*.jpg").Length);
        Assert.True(File.Exists(Path.Combine(assets, "maps", "coords.json")));
        Assert.True(File.Exists(Path.Combine(assets, "TimetableLecturer50.xml")));
        Assert.False(Directory.Exists(Path.Combine(ResourceKeysTests.RepoRoot(), "src", "Vograph")), "the WPF client is gone");
    }
}
