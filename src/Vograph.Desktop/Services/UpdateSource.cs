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
    private readonly AutoUpdateService _service = new();
    public Task<AutoUpdateService.UpdateInfo?> GetLatestAsync(CancellationToken ct = default) => _service.GetLatestAsync("windows");
    public Task DownloadAsync(string url, string destPath, IProgress<double>? progress, CancellationToken ct = default) => _service.DownloadAssetAsync(url, destPath, progress, ct);
}
