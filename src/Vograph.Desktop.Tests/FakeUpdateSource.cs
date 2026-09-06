using Vograph.Core.Services;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Tests;

public sealed class FakeUpdateSource : IUpdateSource
{
    public AutoUpdateService.UpdateInfo? Latest { get; set; }
    public Exception? Failure { get; set; }
    public int Checks { get; private set; }
    public List<string> Downloads { get; } = new();

    public Task<AutoUpdateService.UpdateInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        Checks++;
        if (Failure is not null) throw Failure;
        return Task.FromResult(Latest);
    }

    public Task DownloadAsync(string url, string destPath, IProgress<double>? progress, CancellationToken ct = default)
    {
        Downloads.Add(url);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        progress?.Report(0.5);
        File.WriteAllBytes(destPath, new byte[] { 0x50, 0x4B, 0x05, 0x06 }); // an empty zip's magic
        progress?.Report(1.0);
        return Task.CompletedTask;
    }
}
