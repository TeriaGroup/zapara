using System.Text;
using Vograph.Core.Services;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>Spec §1 «чистка Core»: no hard-coded developer paths, caches under the data directory, one XML decoder.</summary>
public class CoreCleanupTests
{
    [Fact]
    public void Caches_Live_Under_The_Data_Directory()
    {
        using var db = TestDb.Create();
        Assert.StartsWith(db.Dir, db.Services.Maps.CacheDir);
        Assert.Equal(Path.Combine(db.Dir, "maps"), db.Services.Maps.CacheDir);
        Assert.Equal(Path.Combine(db.Dir, "TimetableLecturer50.xml"), db.Services.Lecturers.CachePath);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "maps"), db.Services.Maps.BundledDir);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "TimetableLecturer50.xml"), db.Services.Lecturers.BundledPath);
        Assert.StartsWith(db.Dir, db.Services.Maps.GetCoordsPath());
        Assert.StartsWith(db.Dir, db.Services.Maps.Resolve("493;")!.LocalPath);
        Assert.NotNull(db.Services.Maps.GetBundledPathForUrl(MapService.MapUrls[("ГК", 4)])); // the bundle next to the test binaries
    }

    [Fact]
    public void Core_Has_No_Developer_Paths_Left()
    {
        var core = Path.Combine(ResourceKeysTests.RepoRoot(), "src", "Vograph.Core");
        var offenders = Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains(@"C:\Users\NiLle") || File.ReadAllText(f).Contains("data\", \"runs") || File.ReadAllText(f).Contains("class AutoRefreshService") || File.ReadAllText(f).Contains("class SyncHost"))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Version_Tags_Agree()
    {
        Assert.Equal(AppVersion.Tag, AutoUpdateService.CurrentTagWindows);
        Assert.Equal("windows-v2.0.0", AutoUpdateService.CurrentTagWindows);
    }

    [Fact]
    public void Next_Lesson_Is_Searched_Two_Weeks_Ahead() => Assert.Equal(14, MapService.NextLessonHorizonDays);

    [Theory]
    [InlineData("utf16")]
    [InlineData("utf8bom")]
    [InlineData("utf8")]
    [InlineData("utf16nobom")]
    public void DecodeXml_Handles_Every_Encoding_The_Site_Has_Served(string kind)
    {
        const string xml = "<?xml version=\"1.0\"?><Timetable><Group Number=\"А863С\"/></Timetable>";
        byte[] bytes = kind switch
        {
            "utf16" => Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(xml)).ToArray(),
            "utf8bom" => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(xml)).ToArray(),
            "utf16nobom" => Encoding.Unicode.GetBytes(xml),
            _ => Encoding.UTF8.GetBytes(xml),
        };
        Assert.Equal(xml, ParserService.DecodeXml(bytes));
    }

    [Fact]
    public void Qr_Content_Takes_The_Host_From_The_Caller()
    {
        var small = "{\"Version\":1}";
        Assert.Equal(small, SyncService.GenerateQrContent(small, "192.168.1.5"));
        var big = new string('x', 1500);
        Assert.Equal("http://192.168.1.5:8765/sync#token", SyncService.GenerateQrContent(big, "192.168.1.5"));
    }
}
