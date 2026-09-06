using Avalonia;
using Avalonia.Media.Imaging;
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

    public string? LocalPath(MapInfo map) => map.HasMap && _cached.Contains((map.Building == "ВЦ" ? "ГК" : map.Building, map.Floor)) ? Png() : null;

    public Task<string?> EnsureAsync(MapInfo map, CancellationToken ct = default)
    {
        EnsureCalls++;
        _cached.Add((map.Building == "ВЦ" ? "ГК" : map.Building, map.Floor));
        return Task.FromResult<string?>(Png());
    }

    public (int Cached, int Total) CacheStatus() => (_cached.Count, MapService.MapUrls.Count);

    public Task DownloadAllAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        foreach (var key in MapService.MapUrls.Keys)
        {
            _cached.Add(key);
            progress?.Report($"Cached {key.building} {key.floor}");
            Progress.Add($"{key.building} {key.floor}");
        }
        return Task.CompletedTask;
    }

    private string Png()
    {
        if (_png is not null) return _png;
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "map.png");
        using var bmp = new WriteableBitmap(new PixelSize(200, 100), new Vector(96, 96));
        bmp.Save(path, PngBitmapEncoderOptions.Default);
        return _png = path;
    }
}
