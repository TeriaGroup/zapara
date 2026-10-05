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

    public string CacheDir => _maps.GetMapsCacheDir();

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
        return _maps.GetBundledPathForUrl(map.Url);
    }

    /// <summary>The section fetches a missing plan on its own (opening it, following the next lesson), so this is the
    /// one automatic network call in Maps: with the process switch off it stops at the bundled copy instead.</summary>
    public Task<string?> EnsureAsync(MapInfo map, CancellationToken ct = default) =>
        _allowNetwork() ? _maps.EnsureCachedAsync(map) : Task.FromResult(map.HasMap ? _maps.GetBundledPathForUrl(map.Url) : null);

    public (int Cached, int Total) CacheStatus()
    {
        var (cached, total, _, _) = _maps.GetCacheStatus();
        return (cached, total);
    }

    /// <summary>The «…» menu's «Скачать свежие планы»: nine plans over the wire, and the one door in this section
    /// that used to ignore the process switch — VOGRAPH_OFFLINE=1 promises «no lecturer or map downloads»
    /// (App.axaml.cs) and this went straight to MapService anyway. Gated like EnsureAsync, and with the same
    /// user-visible outcome as the rest of the offline behaviour: the section reads the cache count afterwards
    /// and reports «Скачано N из 9» — no new wording, and nothing pretends the plans arrived.</summary>
    public Task DownloadAllAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        if (!_allowNetwork())
        {
            _log.Info("maps: «Скачать свежие планы» skipped, network disabled for this run");
            return Task.CompletedTask;
        }
        return _maps.EnsureAllMapsCachedAsync(null, progress, preferBundledFirst: true);
    }
}
