using Vograph.Desktop.Services;

namespace Vograph.Desktop.Tests;

public sealed class FakeLauncher : ILauncherService
{
    public List<string> Urls { get; } = new();
    public List<string> Folders { get; } = new();
    public Task OpenUrlAsync(string url) { Urls.Add(url); return Task.CompletedTask; }
    public Task OpenFolderAsync(string path) { Folders.Add(path); return Task.CompletedTask; }
}
