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

    /// <summary>The two local paths are instance state so tests can point the store at files that cannot exist:
    /// the build copies the real directory next to the binaries and Core's cache lives in the user's own profile.</summary>
    public LecturerStore(LecturerService service, AppLog log, string? cachePath = null, string? bundledPath = null)
    {
        _service = service;
        _log = log;
        CachePath = cachePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vograph", "TimetableLecturer50.xml");
        BundledPath = bundledPath ?? Path.Combine(AppContext.BaseDirectory, "TimetableLecturer50.xml");
    }

    public bool IsLoaded => _service.IsLoaded;
    public IReadOnlyList<LecturerInfo> Lecturers => _service.Lecturers;
    public IReadOnlyList<LecturerLesson> Lessons => _service.Lessons;

    /// <summary>Where Core's FetchXmlAsync writes its copy (real LocalAppData: Core ignores VOGRAPH_DATA_DIR here — cleanup is a stage-3 item).</summary>
    public string CachePath { get; }

    /// <summary>The copy the build drops next to the exe, for a first launch with no cache and no network.</summary>
    public string BundledPath { get; }

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
