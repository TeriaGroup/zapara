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
        var xml = RequireTimetable(DecodeXml(bytes));
        return (xml, Convert.ToBase64String(bytes)); // raw as base64 for storage if needed
    }

    /// <summary>voenmeh.ru serves the XML as UTF-16LE with a BOM; archives came as UTF-8 with and without one.
    /// The single decoder for timetables, lecturers and the desktop refresher.</summary>
    public static string DecodeXml(byte[] bytes) => TimetableParser.DecodeXml(bytes);

    public static string RequireTimetable(string xml)
    {
        if (TimetableParser.IsHtml(xml)) throw new InvalidOperationException(TimetableParser.NotTimetable);
        return xml;
    }

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
            var idsWithLessons = new HashSet<string>(lessonsByGroup.Select(g => g.Key));
            foreach (var grp in lessonsByGroup)
            {
                _db.ClearScheduleForGroup(grp.Key);
                foreach (var lesson in grp.OrderBy(l => l.DayOfWeek).ThenBy(l => l.Parity).ThenBy(l => l.Index))
                {
                    _db.InsertLesson(lesson);
                }
            }
            foreach (var g in groups)
            {
                if (!idsWithLessons.Contains(g.Id))
                    _db.ClearScheduleForGroup(g.Id);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        RecomputeHomework();
        return (periodStart, weekCount, periodTitle);
    }

    /// <summary>Selected group plus enabled friends — live JSON fetches these names only, never the whole catalog.</summary>
    public static List<string> NeededGroupNames(Database db)
    {
        var s = db.GetSettings();
        var names = new List<string>();
        var selected = string.IsNullOrEmpty(s.MyGroupId) ? null : db.GetGroup(s.MyGroupId);
        if (selected is { Name.Length: > 0 }) names.Add(selected.Name);
        else if (!string.IsNullOrWhiteSpace(s.MyGroupId)) names.Add(s.MyGroupId);
        foreach (var friend in db.GetFriends())
            if (friend.Enabled && !string.IsNullOrWhiteSpace(friend.GroupName)) names.Add(friend.GroupName);
        return names.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Ingest the live Voenmeh JSON snapshot. Incoming group ids are names; keep existing SQLite ids by name
    /// so homework, overrides and MyGroupId=3313 survive. Lessons are replaced only for groups that were fetched —
    /// a catalog-only payload must not wipe last-good pairs.</summary>
    public (DateTime periodStart, int weekCount, string periodTitle) RefreshParsed(ParsedSchedule parsed)
    {
        using var tx = _db.Connection.BeginTransaction();
        try
        {
            var settings = _db.GetSettings();
            settings.PeriodTitle = parsed.PeriodTitle;
            settings.PeriodStart = parsed.PeriodStart.ToString("yyyy-MM-dd");
            settings.WeekCount = parsed.WeekCount;
            settings.LastFetchedAt = DateTime.UtcNow.ToString("o");
            _db.SaveSettings(settings);

            var existingByName = _db.GetAllGroups()
                .GroupBy(g => g.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var idByIncoming = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var g in parsed.Groups)
                idByIncoming[g.Id] = existingByName.TryGetValue(g.Name, out var kept) ? kept.Id : g.Id;

            var now = DateTime.UtcNow;
            foreach (var g in parsed.Groups)
            {
                _db.UpsertGroup(new Group
                {
                    Id = idByIncoming[g.Id],
                    Name = g.Name,
                    Url = string.IsNullOrWhiteSpace(g.Url) ? VoenmehScheduleClient.Origin : g.Url,
                    LastFetchedAt = now
                });
            }

            var lessonsByIncoming = parsed.Lessons.GroupBy(l => l.GroupId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            foreach (var name in parsed.FetchedGroupNames ?? Array.Empty<string>())
            {
                var storedId = idByIncoming.TryGetValue(name, out var id) ? id
                    : existingByName.TryGetValue(name, out var kept) ? kept.Id : name;
                _db.ClearScheduleForGroup(storedId);
                if (!lessonsByIncoming.TryGetValue(name, out var list)) continue;
                foreach (var lesson in list.OrderBy(l => l.DayOfWeek).ThenBy(l => l.Parity).ThenBy(l => l.Index))
                {
                    lesson.GroupId = storedId;
                    _db.InsertLesson(lesson);
                }
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        RecomputeHomework();
        return (parsed.PeriodStart, parsed.WeekCount, parsed.PeriodTitle);
    }

    private void RecomputeHomework()
    {
        try
        {
            new HomeworkService(_db).RecomputeAllStatuses();
        }
        catch { }
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
