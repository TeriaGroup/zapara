using Vograph.Core.Services;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#39: папка данных не может стать относительным «Vograph» (= исполняемый файл в папке сборки).</summary>
public class DataRootTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\Users\u" : "/home/u";

    [Fact]
    public void Uses_local_app_data_when_the_system_provides_it()
    {
        var local = Path.Combine(Root, ".local", "share");
        Assert.Equal(Path.Combine(local, "Vograph"), VographDataRoot.Resolve(local, null, null, Root));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("relative")]
    public void Falls_back_to_xdg_then_home_then_next_to_the_program(string? local)
    {
        var xdg = Path.Combine(Root, "xdg");
        Assert.Equal(Path.Combine(xdg, "Vograph"), VographDataRoot.Resolve(local, xdg, Root, Root));
        Assert.Equal(Path.Combine(Root, ".local", "share", "Vograph"), VographDataRoot.Resolve(local, "", Root, Root));
        Assert.Equal(Path.Combine(Root, ".local", "share", "Vograph"), VographDataRoot.Resolve(local, "rel/xdg", Root, Root));
        var bin = Path.Combine(Root, "bin");
        Assert.Equal(Path.Combine(bin, "data"), VographDataRoot.Resolve(local, null, null, bin));
        Assert.Equal(Path.Combine(bin, "data"), VographDataRoot.Resolve(local, null, "relative-home", bin));
    }

    [Fact]
    public void Build_folder_with_an_executable_named_Vograph_no_longer_collides()
    {
        // Воспроизведение #39: папка сборки, в ней файл «Vograph», окружение без HOME/XDG.
        var build = Directory.CreateTempSubdirectory("vograph-39-").FullName;
        var cwd = Environment.CurrentDirectory;
        try
        {
            File.WriteAllText(Path.Combine(build, "Vograph"), "executable");
            Environment.CurrentDirectory = build;
            var before = Path.Combine("", "Vograph"); // старый путь при пустом LocalApplicationData
            Assert.Throws<IOException>(() => Directory.CreateDirectory(before));

            var dir = VographDataRoot.Resolve("", null, null, build);
            Assert.True(Path.IsPathFullyQualified(dir));
            Directory.CreateDirectory(dir);
            Assert.True(Directory.Exists(dir));
            Assert.True(File.Exists(Path.Combine(build, "Vograph")));
        }
        finally
        {
            Environment.CurrentDirectory = cwd;
            Directory.Delete(build, true);
        }
    }

    [Fact]
    public void Default_paths_are_absolute_everywhere()
    {
        Assert.True(Path.IsPathFullyQualified(VographDataRoot.DefaultDir));
        Assert.True(Path.IsPathFullyQualified(UpdateChannelStore.FilePath));
    }

    [Fact]
    public void Relative_override_is_made_absolute()
    {
        var keep = Environment.GetEnvironmentVariable(AppPaths.DataDirEnv);
        var tmp = Directory.CreateTempSubdirectory("vograph-39-env-").FullName;
        var cwd = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = tmp;
            Environment.SetEnvironmentVariable(AppPaths.DataDirEnv, "rel-data");
            Assert.Equal(Path.Combine(tmp, "rel-data"), AppPaths.DataDir);
        }
        finally
        {
            Environment.CurrentDirectory = cwd;
            Environment.SetEnvironmentVariable(AppPaths.DataDirEnv, keep);
            Directory.Delete(tmp, true);
        }
    }
}
