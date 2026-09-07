using Vograph.Core.Services;

namespace Vograph.Desktop.Services;

/// <summary>GitHub releases behind an interface: view models never hit the network in tests.</summary>
public interface IUpdateSource
{
    Task<AutoUpdateService.UpdateInfo?> GetLatestAsync(CancellationToken ct = default);
    Task DownloadAsync(string url, string destPath, IProgress<double>? progress, CancellationToken ct = default);
}

public sealed class GitHubUpdateSource : IUpdateSource
{
    private readonly AutoUpdateService _service;
    public GitHubUpdateSource(AutoUpdateService service) => _service = service; // the app's single instance and HttpClient
    /// <summary>The app's single AutoUpdateService (and its HttpClient) — exposed so the wiring can be asserted.</summary>
    internal AutoUpdateService Service => _service;
    public Task<AutoUpdateService.UpdateInfo?> GetLatestAsync(CancellationToken ct = default) => _service.GetLatestAsync("windows", ct);
    public Task DownloadAsync(string url, string destPath, IProgress<double>? progress, CancellationToken ct = default) => _service.DownloadAssetAsync(url, destPath, progress, ct);
}
