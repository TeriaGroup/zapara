using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Vograph.Desktop.Services;

/// <summary>Opens things outside the app. Abstracted so view models and tests never touch the OS shell.</summary>
public interface ILauncherService
{
    Task OpenUrlAsync(string url);
    Task OpenFolderAsync(string path);
}

/// <summary>Default in AppServices (unit tests, headless): only logs.</summary>
public sealed class NullLauncher : ILauncherService
{
    private readonly AppLog _log;
    public NullLauncher(AppLog log) => _log = log;
    public Task OpenUrlAsync(string url) { _log.Info($"launcher (noop): {url}"); return Task.CompletedTask; }
    public Task OpenFolderAsync(string path) { _log.Info($"launcher (noop): {path}"); return Task.CompletedTask; }
}

/// <summary>Real app: TopLevel.Launcher (system browser / Explorer). Failures are logged, never thrown.</summary>
public sealed class AvaloniaLauncher : ILauncherService
{
    private readonly Func<TopLevel?> _topLevel;
    private readonly AppLog _log;

    public AvaloniaLauncher(Func<TopLevel?> topLevel, AppLog log)
    {
        _topLevel = topLevel;
        _log = log;
    }

    public async Task OpenUrlAsync(string url)
    {
        try
        {
            if (_topLevel() is { } tl && !await tl.Launcher.LaunchUriAsync(new Uri(url))) _log.Warn($"launcher refused {url}");
        }
        catch (Exception ex) { _log.Error("launcher url", ex); }
    }

    public async Task OpenFolderAsync(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            if (_topLevel() is { } tl && !await tl.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path))) _log.Warn($"launcher refused {path}");
        }
        catch (Exception ex) { _log.Error("launcher folder", ex); }
    }
}
