using System.IO;
using System.Text;
using System.Xml;
using Vograph.Core.Models;
using Vograph.Timetable;

namespace Vograph.Core.Services;


public class LecturerService
{
    public const string DefaultUrl = "https://voenmeh.ru/wp-content/themes/Avada-Child-Theme-Voenmeh/_voenmeh_grafics/TimetableLecturer50.xml";
    private List<LecturerInfo> _lecturers = new();
    private List<LecturerLesson> _lessons = new();
    private bool _loaded = false;

    private HttpClient? _http;
    private HttpClient Http => _http ??= ParserService.CreateHttpClient();

    /// <param name="cachePath">Where a downloaded copy is kept (the desktop passes VOGRAPH_DATA_DIR\TimetableLecturer50.xml).</param>
    /// <param name="bundledPath">The copy shipped next to the exe for a first offline start.</param>
    public LecturerService(Database? db = null, string? cachePath = null, string? bundledPath = null)
    {
        _ = db;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        CachePath = cachePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vograph", "TimetableLecturer50.xml");
        BundledPath = bundledPath ?? Path.Combine(AppContext.BaseDirectory, "TimetableLecturer50.xml");
    }

    public string CachePath { get; }
    public string BundledPath { get; }

    public IReadOnlyList<LecturerInfo> Lecturers => _lecturers;
    public IReadOnlyList<LecturerLesson> Lessons => _lessons;
    public bool IsLoaded => _loaded;

    public async Task<(string xml, bool fromCache)> FetchXmlAsync(string url = DefaultUrl, HttpClient? client = null)
    {
        var http = client ?? Http;
        try
        {
            var bytes = await http.GetByteArrayAsync(url);
            var xml = ParserService.DecodeXml(bytes);
            if (TimetableParser.IsHtml(xml)) throw new InvalidOperationException(TimetableParser.NotTimetable);
            try { Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!); await File.WriteAllTextAsync(CachePath, xml, Encoding.UTF8); } catch {}
            return (xml, false);
        }
        catch
        {
            if (File.Exists(CachePath))
            {
                try { var cached = await File.ReadAllTextAsync(CachePath, Encoding.UTF8); return (cached, true); } catch {}
            }
            throw;
        }
    }

    public async Task LoadAsync(string? xmlOverride = null)
    {
        string xml;
        if (xmlOverride != null) xml = xmlOverride;
        else
        {
            if (File.Exists(CachePath))
            {
                try { xml = await File.ReadAllTextAsync(CachePath, Encoding.UTF8); Parse(xml); _loaded = true; } catch {}
            }
            if (!_loaded)
            {
                var bundled = BundledPath;
                if (File.Exists(bundled))
                {
                    try { xml = await File.ReadAllTextAsync(bundled, Encoding.UTF8); Parse(xml); _loaded = true; try { Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!); File.Copy(bundled, CachePath, true); } catch {} } catch {}
                }
            }
            try
            {
                var (fetched, fromCache) = await FetchXmlAsync();
                if (!fromCache)
                {
                    Parse(fetched);
                    _loaded = true;
                }
            }
            catch
            {
                if (!_loaded && File.Exists(CachePath))
                {
                    var cached = await File.ReadAllTextAsync(CachePath, Encoding.UTF8);
                    Parse(cached);
                    _loaded = true;
                }
                else if (!_loaded)
                {
                    var bundled2 = BundledPath;
                    if (File.Exists(bundled2))
                    {
                        var bxml = await File.ReadAllTextAsync(bundled2, Encoding.UTF8);
                        Parse(bxml);
                        _loaded = true;
                    }
                }
            }
            return;
        }
        Parse(xml);
        _loaded = true;
    }

    public void Parse(string xml)
    {
        var parsed = LecturerXmlParser.Parse(xml);
        _lecturers = parsed.Lecturers;
        _lessons = parsed.Lessons;
    }

    public List<LecturerInfo> Search(string query, bool onlyMyTeachers = false, HashSet<string>? myTeacherIds = null)
    {
        if (string.IsNullOrWhiteSpace(query)) return onlyMyTeachers && myTeacherIds != null ? _lecturers.Where(l => myTeacherIds.Contains(l.Id) || myTeacherIds.Contains(l.Name)).ToList() : _lecturers.Take(100).ToList();
        var q = query.Trim().ToLowerInvariant();
        var res = _lecturers.Where(l =>
            l.Name.ToLowerInvariant().Contains(q) ||
            l.Id.Contains(q) ||
            (l.Kafedra != null && l.Kafedra.ToLowerInvariant().Contains(q))
        );
        if (onlyMyTeachers && myTeacherIds != null)
            res = res.Where(l => myTeacherIds.Contains(l.Id) || myTeacherIds.Contains(l.Name) || myTeacherIds.Any(id => l.Name.Contains(id)));
        return res.OrderBy(l => l.Name).Take(100).ToList();
    }

    public List<LecturerLesson> GetLessonsForLecturer(string lecturerIdOrName)
    {
        return _lessons.Where(l => l.LecturerId == lecturerIdOrName || l.LecturerName.Equals(lecturerIdOrName, StringComparison.OrdinalIgnoreCase)).OrderBy(l => l.DayOfWeek).ThenBy(l => l.Parity).ThenBy(l => l.TimeStart).ToList();
    }

    public HashSet<string> GetMyTeacherIds(string groupId, Database db)
    {
        var set = new HashSet<string>();
        try
        {
            var lessons = db.GetAllLessonsForGroup(groupId);
            foreach (var l in lessons)
            {
                if (string.IsNullOrWhiteSpace(l.TeacherRaw)) continue;
                var teachers = l.TeacherRaw.Split(';').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t));
                foreach (var t in teachers)
                {
                    var match = _lecturers.FirstOrDefault(li => li.Name.Contains(t.Split(' ')[0]) || t.Contains(li.Name.Split(' ')[0]));
                    if (match != null) set.Add(match.Id);
                    set.Add(t); // also add ShortName itself for fallback
                }
            }
        }
        catch {}
        return set;
    }
}
