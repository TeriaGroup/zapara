using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class UiPrefsTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"), "ui.json");

    [Fact]
    public void Load_Missing_File_Returns_Defaults()
    {
        var prefs = UiPrefs.Load(TempFile());

        Assert.Equal(ThemeChoice.System, prefs.Theme);
        Assert.False(prefs.SidebarCollapsed);
        Assert.True(prefs.Animations);
        Assert.Null(prefs.Window);
    }

    [Fact]
    public void Save_Then_Load_Round_Trips()
    {
        var path = TempFile();
        var prefs = UiPrefs.Load(path);
        prefs.Theme = ThemeChoice.Dark;
        prefs.SidebarCollapsed = true;
        prefs.Animations = false;
        prefs.Window = new WindowBounds(10, 20, 1300, 820, Maximized: true);
        prefs.Save();

        var loaded = UiPrefs.Load(path);

        Assert.Equal(ThemeChoice.Dark, loaded.Theme);
        Assert.True(loaded.SidebarCollapsed);
        Assert.False(loaded.Animations);
        Assert.Equal(new WindowBounds(10, 20, 1300, 820, true), loaded.Window);
        Assert.Contains("\"Theme\": \"Dark\"", File.ReadAllText(path)); // enums as names, hand-editable
    }

    [Fact]
    public void Corrupt_File_Falls_Back_To_Defaults()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        var prefs = UiPrefs.Load(path);

        Assert.Equal(ThemeChoice.System, prefs.Theme);
        Assert.Equal(path, prefs.FilePath);
    }

    [Fact]
    public void AppPaths_Respect_Env_Override()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(AppPaths.DataDirEnv, dir);
        try
        {
            Assert.Equal(dir, AppPaths.DataDir);
            Assert.Equal(Path.Combine(dir, "vograph.db"), AppPaths.DbPath);
            Assert.Equal(Path.Combine(dir, "ui.json"), AppPaths.UiPrefsPath);
            Assert.Equal(Path.Combine(dir, "logs"), AppPaths.LogsDir);
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirEnv, null);
        }
    }
}
