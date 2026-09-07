using System.Text;
using System.Xml;
using Vograph.Core.Models;

namespace Vograph.Core.Services;

// Represents a lecturer from TimetableLecturer50.xml
public class LecturerInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = ""; // LecturerName
    public string Kafedra { get; set; } = "";
    public string ShortName { get; set; } = ""; // derived or from group XML
}

public class LecturerLesson
{
    public string LecturerId { get; set; } = "";
    public string LecturerName { get; set; } = "";
    public string Kafedra { get; set; } = "";
    public int DayOfWeek { get; set; } // 1..6
    public int Parity { get; set; } // 1 odd, 2 even
    public string TimeStart { get; set; } = "";
    public string TimeEnd { get; set; } = "";
    public string DisciplineRaw { get; set; } = ""; // full Discipline
    public string TypeRaw { get; set; } = "";
    public string SubjectRaw { get; set; } = ""; // without type
    public string SubjectNormalized { get; set; } = "";
    public string ClassroomRaw { get; set; } = "";
    public string RoomRaw { get; set; } = "";
    public string BuildingRaw { get; set; } = "";
    public List<GroupRef> Groups { get; set; } = new();
}

public class GroupRef
{
    public string IdGroup { get; set; } = "";
    public string Number { get; set; } = "";
}

public class LecturerService
{
    private readonly Database? _db;
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
        _db = db;
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
            // try cache first for offline
            if (File.Exists(CachePath))
            {
                try { xml = await File.ReadAllTextAsync(CachePath, Encoding.UTF8); Parse(xml); _loaded = true; } catch {}
            }
            // try bundled (publish/TimetableLecturer50.xml) for first offline launch
            if (!_loaded)
            {
                var bundled = BundledPath;
                if (File.Exists(bundled))
                {
                    try { xml = await File.ReadAllTextAsync(bundled, Encoding.UTF8); Parse(xml); _loaded = true; try { Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!); File.Copy(bundled, CachePath, true); } catch {} } catch {}
                }
            }
            // try fetch in background (if online, update)
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
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        var dayMap = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Понедельник"]=1, ["Вторник"]=2, ["Среда"]=3, ["Четверг"]=4, ["Пятница"]=5, ["Суббота"]=6, ["Воскресенье"]=7,
            // also handle garbled due to encoding? but doc should be correct UTF8
        };

        _lecturers.Clear();
        _lessons.Clear();

        var lecturerNodes = doc.SelectNodes("/Timetable/Lecturer");
        if (lecturerNodes == null) return;
        foreach (XmlNode ln in lecturerNodes)
        {
            var id = ln.Attributes?["IdLecturer"]?.Value ?? "";
            var name = ln.Attributes?["LecturerName"]?.Value ?? "";
            var kaf = ln.Attributes?["Kafedra"]?.Value ?? "";
            if (string.IsNullOrEmpty(id)) continue;
            _lecturers.Add(new LecturerInfo { Id = id, Name = name, Kafedra = kaf });

            var daysNode = ln.SelectSingleNode("Days");
            if (daysNode == null) continue;
            var dayNodes = daysNode.SelectNodes("Day");
            if (dayNodes == null) continue;
            foreach (XmlNode dayNode in dayNodes)
            {
                var dayTitle = dayNode.Attributes?["Title"]?.Value ?? "";
                int dayNum = dayMap.TryGetValue(dayTitle, out var v) ? v : 0;
                // Fallback: try DayTitle inside Lesson
                var lessonNodes = dayNode.SelectNodes("LecturerLessons/Lesson");
                if (lessonNodes == null) continue;
                foreach (XmlNode lnode in lessonNodes)
                {
                    var wcStr = lnode.SelectSingleNode("WeekCode")?.InnerText?.Trim() ?? "0";
                    int.TryParse(wcStr, out var parity);
                    var timeRaw = lnode.SelectSingleNode("Time")?.InnerText?.Trim() ?? "";
                    var discRaw = lnode.SelectSingleNode("Discipline")?.InnerText?.Trim() ?? "";
                    var classroomRaw = lnode.SelectSingleNode("Classroom")?.InnerText?.Trim() ?? "";

                    // Correct dayNum if needed from DayTitle
                    if (dayNum == 0)
                    {
                        var dt = lnode.SelectSingleNode("DayTitle")?.InnerText?.Trim() ?? dayTitle;
                        dayMap.TryGetValue(dt, out dayNum);
                    }
                    if (dayNum == 0) continue;

                    string typeRaw = "";
                    string subjectRaw = discRaw;
                    if (!string.IsNullOrWhiteSpace(discRaw))
                    {
                        var parts = discRaw.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2)
                        {
                            var first = parts[0].ToLowerInvariant();
                            if (first is "лек" or "пр" or "лаб" or "конс" or "зач" or "экз" or "курс" or "практика")
                            {
                                typeRaw = parts[0];
                                subjectRaw = parts[1];
                            }
                        }
                    }
                    string timeStart = "";
                    string timeEnd = "";
                    if (!string.IsNullOrWhiteSpace(timeRaw))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(timeRaw, @"(\d{1,2}:\d{2})");
                        if (m.Success) timeStart = m.Groups[1].Value.PadLeft(5,'0');
                        if (!string.IsNullOrEmpty(timeStart) && TimeSpan.TryParse(timeStart, out var ts))
                        {
                            var te = ts.Add(TimeSpan.FromMinutes(95));
                            timeEnd = te.ToString(@"hh\:mm");
                        }
                    }
                    string roomRaw = "";
                    string buildingRaw = "";
                    if (!string.IsNullOrWhiteSpace(classroomRaw))
                    {
                        var raw = classroomRaw.Trim().TrimEnd(';').Trim();
                        if (raw.Equals("дистанционно", StringComparison.OrdinalIgnoreCase))
                        {
                            roomRaw = raw;
                        }
                        else
                        {
                            var clean = raw.Replace("*", "").Trim();
                            var parts = clean.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && parts[0].Any(char.IsLetter))
                            {
                                buildingRaw = parts[0];
                                roomRaw = string.Join(" ", parts.Skip(1)).TrimEnd(';');
                            }
                            else
                            {
                                roomRaw = clean;
                                buildingRaw = raw.Contains("*") ? "УЛК" : "ГК"; // star=УЛК per correction
                                if (raw.Contains("ВЦ")) buildingRaw = "ВЦ";
                            }
                        }
                    }

                    var groups = new List<GroupRef>();
                    var groupNodes = lnode.SelectNodes("Groups/Group");
                    if (groupNodes != null)
                    {
                        foreach (XmlNode gn in groupNodes)
                        {
                            var gid = gn.SelectSingleNode("IdGroup")?.InnerText?.Trim() ?? "";
                            var gnum = gn.SelectSingleNode("Number")?.InnerText?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(gid) || !string.IsNullOrEmpty(gnum))
                                groups.Add(new GroupRef { IdGroup = gid, Number = gnum });
                        }
                    }

                    var ll = new LecturerLesson
                    {
                        LecturerId = id,
                        LecturerName = name,
                        Kafedra = kaf,
                        DayOfWeek = dayNum,
                        Parity = parity,
                        TimeStart = timeStart,
                        TimeEnd = timeEnd,
                        DisciplineRaw = discRaw,
                        TypeRaw = typeRaw,
                        SubjectRaw = subjectRaw,
                        SubjectNormalized = ParityService.NormalizeSubject(discRaw),
                        ClassroomRaw = classroomRaw,
                        RoomRaw = roomRaw,
                        BuildingRaw = buildingRaw,
                        Groups = groups
                    };
                    _lessons.Add(ll);
                }
            }
        }

        // Also try to fill ShortName from group XML if available: match via IdLecturer -> ShortName
        // ShortName mapping can be enriched later via ParserService's group lessons
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
        // Collect IdLecturer from group lessons for this groupId
        var set = new HashSet<string>();
        try
        {
            // We need to parse group XML or use schedule_cache's teacherRaw? But we need Ids.
            // Instead, collect ShortNames from schedule_cache and map to LecturerInfo via name contains
            var lessons = db.GetAllLessonsForGroup(groupId);
            foreach (var l in lessons)
            {
                if (string.IsNullOrWhiteSpace(l.TeacherRaw)) continue;
                var teachers = l.TeacherRaw.Split(';').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t));
                foreach (var t in teachers)
                {
                    // Find lecturer by ShortName contains
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
