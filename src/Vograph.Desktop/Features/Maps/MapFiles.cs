using Vograph.Core.Services;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Maps;

/// <summary>Where the plan images live. File/network only — run with Task.Run, outside the Core gate.</summary>
public interface IMapFiles
{
    string CacheDir { get; }
    /// <summary>A readable local copy (cache, else the bundled maps\ folder), or null.</summary>
    string? LocalPath(MapInfo map);
    /// <summary>Copies the bundled file or downloads; null when neither is possible.</summary>
    Task<string?> EnsureAsync(MapInfo map, CancellationToken ct = default);
    (int Cached, int Total) CacheStatus();
    Task DownloadAllAsync(IProgress<string>? progress, CancellationToken ct = default);
}

public sealed class MapFiles : IMapFiles
{
    private readonly MapService _maps;
    private readonly AppLog _log;
    private readonly Func<bool> _allowNetwork;

    public MapFiles(MapService maps, AppLog log, Func<bool> allowNetwork)
    {
        _maps = maps;
        _log = log;
        _allowNetwork = allowNetwork;
    }

    public string CacheDir => MapService.GetMapsCacheDir();

    public string? LocalPath(MapInfo map)
    {
        if (!map.HasMap || string.IsNullOrEmpty(map.Url)) return null;
        try
        {
            if (File.Exists(map.LocalPath) && new FileInfo(map.LocalPath).Length > 1000) return map.LocalPath;
        }
        catch (Exception ex)
        {
            _log.Warn($"map file {map.LocalPath}: {ex.GetType().Name}: {ex.Message}"); // unreadable cache entry: fall back to the bundled copy
        }
        return MapService.GetBundledPathForUrl(map.Url);
    }

    /// <summary>The section fetches a missing plan on its own (opening it, following the next lesson), so this is the
    /// one automatic network call in Maps: with the process switch off it stops at the bundled copy instead.</summary>
    public Task<string?> EnsureAsync(MapInfo map, CancellationToken ct = default) =>
        _allowNetwork() ? _maps.EnsureCachedAsync(map) : Task.FromResult(map.HasMap ? MapService.GetBundledPathForUrl(map.Url) : null);

    public (int Cached, int Total) CacheStatus()
    {
        var (cached, total, _, _) = _maps.GetCacheStatus();
        return (cached, total);
    }

    public Task DownloadAllAsync(IProgress<string>? progress, CancellationToken ct = default) =>
        _maps.EnsureAllMapsCachedAsync(null, progress, preferBundledFirst: true);
}
