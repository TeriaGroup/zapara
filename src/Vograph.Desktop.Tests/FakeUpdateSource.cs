using System.IO.Compression;
using Vograph.Core.Services;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Tests;

public sealed class FakeUpdateSource : IUpdateSource
{
    public AutoUpdateService.UpdateInfo? Latest { get; set; }
    public Exception? Failure { get; set; }
    public Exception? DownloadFailure { get; set; }
    /// <summary>Write bytes that are not a zip archive (a truncated download, an HTML error page saved as .zip).</summary>
    public bool Corrupt { get; set; }
    /// <summary>Write a mis-built release: the same files one folder down. UpdateRunner unpacks flat and starts
    /// {dir}\Vograph.exe, so such an archive must be refused before anything is installed on its account.</summary>
    public bool Nested { get; set; }
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
        if (DownloadFailure is not null) throw DownloadFailure;
        Downloads.Add(url);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        progress?.Report(0.5);
        File.WriteAllBytes(destPath, Corrupt ? new byte[4096] : ReleaseZip(Nested));
        progress?.Report(1.0);
        return Task.CompletedTask;
    }

    /// <summary>What a real release zip looks like to the installer's check: an archive with Vograph.exe inside.</summary>
    /// <param name="nested">Put the payload one folder down instead — the mis-built release the check must refuse.</param>
    public static byte[] ReleaseZip(bool nested = false)
    {
        var prefix = nested ? "ZAPARA_win-x64/" : "";
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Stored, not deflated: 5 KB of zeros would compress below LooksLikeZip's 1 KB floor.
            // Each entry stream is closed (explicit braces, not a block-scoped `using var`) before the next
            // CreateEntry call — ZipArchive refuses to open a second entry while an earlier one is still open.
            using (var exe = zip.CreateEntry(prefix + "Vograph.exe", CompressionLevel.NoCompression).Open())
                exe.Write(new byte[4096]);
            using (var dll = zip.CreateEntry(prefix + "Vograph.Core.dll", CompressionLevel.NoCompression).Open())
                dll.Write(new byte[1024]);
        }
        return ms.ToArray();
    }
}
