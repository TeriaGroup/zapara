using System.Text;
using Vograph.Core.Models;
using Vograph.Timetable;

namespace Vograph.Core.Services;

public class ParserService
{
    private readonly Database _db;
    public const string DefaultUrl = TimetableParser.DefaultUrl;

    public ParserService(Database db)
    {
        _db = db;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private HttpClient? _http;
    private HttpClient Http => _http ??= CreateHttpClient();

    /// <summary>One client per service instance (was one per call): the same UA header, no socket churn.</summary>
    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Vograph/2.0");
        return http;
    }

    public async Task<(string xml, string raw)> FetchXmlAsync(string url = DefaultUrl, HttpClient? client = null)
    {
        var bytes = await (client ?? Http).GetByteArrayAsync(url);
        return (DecodeXml(bytes), Convert.ToBase64String(bytes)); // raw as base64 for storage if needed
    }

    /// <summary>voenmeh.ru serves the XML as UTF-16LE with a BOM; archives came as UTF-8 with and without one.
    /// The single decoder for timetables, lecturers and the desktop refresher.</summary>
    public static string DecodeXml(byte[] bytes) => TimetableParser.DecodeXml(bytes);

    public (List<Group> groups, List<Lesson> lessons, DateTime periodStart, int weekCount, string periodTitle) Parse(string xml)
        => new TimetableParser().Parse(xml);

    public async Task<(DateTime periodStart, int weekCount, string periodTitle)> RefreshAsync(string url = DefaultUrl, string? xmlOverride = null)
    {
        string xml;
        if (xmlOverride != null)
        {
            xml = xmlOverride;
        }
        else
        {
            var (fetched, _) = await FetchXmlAsync(url);
            xml = fetched;
        }

        var (groups, lessons, periodStart, weekCount, periodTitle) = Parse(xml);

        // Preserve overrides/homework (do not delete them) — only refresh schedule_cache and groups
        // Use transaction: clear schedule_cache per group, upsert groups, insert lessons
        using var tx = _db.Connection.BeginTransaction();
        try
        {
            // Save period info to settings
            var settings = _db.GetSettings();
            settings.PeriodTitle = periodTitle;
            settings.PeriodStart = periodStart.ToString("yyyy-MM-dd");
            settings.WeekCount = weekCount;
            settings.LastFetchedAt = DateTime.UtcNow.ToString("o");
            _db.SaveSettings(settings);

            // Upsert groups first
            foreach (var g in groups)
            {
                g.LastFetchedAt = DateTime.UtcNow;
                g.Url = url;
                // keep rawXml for groups with schedule
                if (lessons.Any(l => l.GroupId == g.Id))
                {
                    // store raw group xml snippet? Not needed
                }
                _db.UpsertGroup(g);
            }

            // Group lessons by groupId to clear and reinsert per group
            var lessonsByGroup = lessons.GroupBy(l => l.GroupId);
            foreach (var grp in lessonsByGroup)
            {
                _db.ClearScheduleForGroup(grp.Key);
                foreach (var lesson in grp.OrderBy(l => l.DayOfWeek).ThenBy(l => l.Parity).ThenBy(l => l.Index))
                {
                    _db.InsertLesson(lesson);
                }
            }

            // For groups that existed but now have zero lessons (maybe empty), ensure schedule cleared? Already cleared only if in lessonsByGroup.
            // If a group had no Days (like just listing), we already upserted but not cleared; leave as is (no schedule).

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        // Recompute homework due dates after schedule change (per spec: recompute on every cache update, never delete overrides/homework)
        try
        {
            var hwService = new HomeworkService(_db);
            hwService.RecomputeAllStatuses();
        }
        catch { }

        return (periodStart, weekCount, periodTitle);
    }

    public async Task<string> FetchAndCacheRawAsync(string destPath, string url = DefaultUrl)
    {
        var (xml, _) = await FetchXmlAsync(url);
        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(destPath, xml, Encoding.UTF8);
        return xml;
    }
}
