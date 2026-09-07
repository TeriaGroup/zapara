using System.Text;
using Vograph.Core.Services;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Teachers;

/// <summary>
/// Cache-first access to the lecturer timetable. Core's LecturerService.LoadAsync() would await the network
/// before returning even when a cache exists; here the local copy is parsed first and the refresh runs behind it.
/// File and network work only — no SQLite — so callers run it with Task.Run, outside the Core gate.
/// </summary>
public sealed class LecturerStore
{
    private readonly LecturerService _service;
    private readonly AppLog _log;

    public LecturerStore(LecturerService service, AppLog log)
    {
        _service = service;
        _log = log;
    }

    public bool IsLoaded => _service.IsLoaded;
    public IReadOnlyList<LecturerInfo> Lecturers => _service.Lecturers;
    public IReadOnlyList<LecturerLesson> Lessons => _service.Lessons;

    /// <summary>Where Core keeps a downloaded copy — under the data directory since the stage-3 cleanup.</summary>
    public string CachePath => _service.CachePath;
    public string BundledPath => _service.BundledPath;

    /// <summary>Parses the cached copy, else the bundled one. False when neither exists or parses.</summary>
    public async Task<bool> LoadLocalAsync(CancellationToken ct = default)
    {
        foreach (var path in new[] { CachePath, BundledPath })
        {
            if (!File.Exists(path)) continue;
            try
            {
                await _service.LoadAsync(await File.ReadAllTextAsync(path, Encoding.UTF8, ct));
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("lecturers local", ex);
            }
        }
        return false;
    }

    /// <summary>Downloads a fresh copy (Core also writes it to its own cache). False when offline or Core fell back to its cache.</summary>
    public async Task<bool> RefreshAsync(HttpClient? client = null)
    {
        try
        {
            var (xml, fromCache) = await _service.FetchXmlAsync(LecturerService.DefaultUrl, client);
            if (fromCache) return false;
            await _service.LoadAsync(xml);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn($"lecturers refresh: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Tests and imports: parse the given XML directly.</summary>
    public Task LoadXmlAsync(string xml) => _service.LoadAsync(xml);
}
