using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Maps;

namespace Vograph.Desktop.Tests;

/// <summary>Maps «on disk» are a 200×100 PNG rendered once into the test's temp dir; nothing touches %LocalAppData%.</summary>
public sealed class FakeMapFiles : IMapFiles
{
    private readonly string _dir;
    private readonly HashSet<(string, int)> _cached;
    private string? _png;

    public FakeMapFiles(string dir, params (string Building, int Floor)[] cached)
    {
        _dir = dir;
        _cached = cached.ToHashSet();
    }

    public string CacheDir => _dir;
    public List<string> Progress { get; } = new();
    public int EnsureCalls { get; private set; }

    /// <summary>An unreadable maps folder (denied profile, a locked file): the probes throw like the real ones can.</summary>
    public bool ThrowOnStatus { get; set; }
    public bool ThrowOnLocalPath { get; set; }

    /// <summary>No copy anywhere: LocalPath and EnsureAsync both come back empty (an offline first start).</summary>
    public bool EnsureFails { get; set; }

    /// <summary>One plan that «Скачать свежие планы» cannot fetch (a 404 on the site) — the partial-download toast.</summary>
    public (string Building, int Floor)? SkipOnDownload { get; set; }

    public string? LocalPath(MapInfo map)
    {
        if (ThrowOnLocalPath) throw new IOException("maps folder unreadable");
        return map.HasMap && _cached.Contains((map.Building == "ВЦ" ? "ГК" : map.Building, map.Floor)) ? Png() : null;
    }

    public Task<string?> EnsureAsync(MapInfo map, CancellationToken ct = default)
    {
        EnsureCalls++;
        if (EnsureFails) return Task.FromResult<string?>(null);
        _cached.Add((map.Building == "ВЦ" ? "ГК" : map.Building, map.Floor));
        return Task.FromResult<string?>(Png());
    }

    public (int Cached, int Total) CacheStatus() =>
        ThrowOnStatus ? throw new IOException("maps folder unreadable") : (_cached.Count, MapService.MapUrls.Count);

    public Task DownloadAllAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        foreach (var key in MapService.MapUrls.Keys)
        {
            if (SkipOnDownload is { } skip && skip == key) { progress?.Report($"Failed {key.building} {key.floor}"); continue; }
            _cached.Add(key);
            progress?.Report($"Cached {key.building} {key.floor}");
            Progress.Add($"{key.building} {key.floor}");
        }
        return Task.CompletedTask;
    }

    /// <summary>A 200×100 «plan»: light paper with a 20px grid, so a committed frame shows the picture and the
    /// highlight on top of it rather than window chrome alone (the earlier transparent bitmap was invisible).</summary>
    private string Png()
    {
        if (_png is not null) return _png;
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "map.png");
        using var bmp = new WriteableBitmap(new PixelSize(200, 100), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bmp.Lock())
        {
            for (var y = 0; y < 100; y++)
                for (var x = 0; x < 200; x++)
                {
                    var line = x % 20 == 0 || y % 20 == 0;
                    Marshal.WriteInt32(fb.Address + y * fb.RowBytes + x * 4, unchecked((int)(line ? 0xFFD4D4D4 : 0xFFF7F7F7)));
                }
        }
        bmp.Save(path, PngBitmapEncoderOptions.Default);
        return _png = path;
    }
}
