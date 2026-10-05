namespace Vograph.Desktop.Services;

/// <summary>Where the desktop client keeps its data. Same folder as the WPF client, so users keep their DB.</summary>
public static class AppPaths
{
    public const string DataDirEnv = "VOGRAPH_DATA_DIR";

    public static string DataDir
    {
        get
        {
            var env = Environment.GetEnvironmentVariable(DataDirEnv);
            var dir = string.IsNullOrWhiteSpace(env)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vograph")
                : env;
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DbPath => Path.Combine(DataDir, "vograph.db");
    public static string UiPrefsPath => Path.Combine(DataDir, "ui.json");
    public static string LogsDir => Path.Combine(DataDir, "logs");
}
