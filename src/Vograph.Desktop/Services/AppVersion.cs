namespace Vograph.Desktop.Services;

/// <summary>The client's own release tag, derived from the csproj Version (2.0.0 → windows-v2.0.0). Compared with GitHub
/// tags through AutoUpdateService.IsNewer; Core's CurrentTagWindows still names the WPF release and is not used here.</summary>
public static class AppVersion
{
    public static string Short { get; } = typeof(AppVersion).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
    public static string Tag => "windows-v" + Short;
}
