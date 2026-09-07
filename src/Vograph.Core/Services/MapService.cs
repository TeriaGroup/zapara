using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vograph.Core.Models;

namespace Vograph.Core.Services;

public class MapInfo
{
    public string Building { get; set; } = ""; // ГК, УЛК, ВЦ, дистанционно
    public int Floor { get; set; } // 1..5
    public string Title { get; set; } = "";
    public string Url { get; set; } = ""; // remote url
    public string LocalPath { get; set; } = ""; // cached file path
    public string RoomRaw { get; set; } = "";
    public string ClassroomRaw { get; set; } = "";
    public bool IsRemote { get; set; } // дистанционно
    public bool HasMap { get; set; }
    public string Note { get; set; } = "";
}

public class CoordsRect
{
    public double x { get; set; }
    public double y { get; set; }
    public double w { get; set; }
    public double h { get; set; }
}

public class MapService
{
    public const string BaseUrl = "https://voenmeh.ru/wp-content/uploads/2024/09/";
    // Map filenames from https://voenmeh.ru/openmap/
    public static readonly Dictionary<(string building, int floor), string> MapUrls = new()
    {
        [("ГК", 1)] = BaseUrl + "karta-glavnyj-korpus-1-etazh-2022.jpg",
        [("ГК", 2)] = BaseUrl + "karta-glavnyj-korpus-2-etazh-2022.jpg",
        [("ГК", 3)] = BaseUrl + "karta-glavnyj-korpus-3-etazh-2022.jpg",
        [("ГК", 4)] = BaseUrl + "karta-glavnyj-korpus-4-etazh-2022.jpg",
        [("УЛК", 1)] = BaseUrl + "karta-ulk.-1-etazh-2022.jpg",
        [("УЛК", 2)] = BaseUrl + "karta-ulk.-2-etazh-2022.jpg",
        [("УЛК", 3)] = BaseUrl + "karta-ulk.-3-etazh-2022.jpg",
        [("УЛК", 4)] = BaseUrl + "karta-ulk.-4-etazh-2022.jpg",
        [("УЛК", 5)] = BaseUrl + "karta-ulk.-5-etazh-2022.jpg",
    };

    private readonly Database _db;
    private readonly ScheduleService _schedule;
    private Dictionary<string, Dictionary<string, CoordsRect>> _coords = new(StringComparer.OrdinalIgnoreCase);
    private bool _coordsLoaded = false;

    /// <summary>How far GetNextLesson looks ahead (spec §5.1: the empty day hints at the next lesson within two weeks).</summary>
    public const int NextLessonHorizonDays = 14;

    private HttpClient? _http;
    private HttpClient Http => _http ??= ParserService.CreateHttpClient();

    /// <param name="cacheDir">Downloaded plans and the editable coords.json (the desktop passes VOGRAPH_DATA_DIR\maps).</param>
    /// <param name="bundledDir">The maps\ folder shipped next to the exe.</param>
    public MapService(Database db, ScheduleService schedule, string? cacheDir = null, string? bundledDir = null)
    {
        _db = db;
        _schedule = schedule;
        CacheDir = cacheDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vograph", "maps");
        BundledDir = bundledDir ?? Path.Combine(AppContext.BaseDirectory, "maps");
        LoadCoords();
    }

    public string CacheDir { get; }
    public string BundledDir { get; }

    public string GetMapsCacheDir()
    {
        Directory.CreateDirectory(CacheDir);
        return CacheDir;
    }

    public string GetLocalPathForUrl(string url) => Path.Combine(GetMapsCacheDir(), Path.GetFileName(new Uri(url).LocalPath));

    public string? GetBundledPathForUrl(string url)
    {
        try
        {
            var bundled = Path.Combine(BundledDir, Path.GetFileName(new Uri(url).LocalPath));
            return File.Exists(bundled) ? bundled : null;
        }
        catch (UriFormatException) { return null; }
    }

    public string GetCoordsPath() => Path.Combine(GetMapsCacheDir(), "coords.json");

    public string? GetBundledCoordsPath()
    {
        var bundled = Path.Combine(BundledDir, "coords.json");
        return File.Exists(bundled) ? bundled : null;
    }

    private void LoadCoords()
    {
        try
        {
            string? path = null;
            var local = GetCoordsPath();
            if (File.Exists(local)) path = local;
            else
            {
                var bundled = GetBundledCoordsPath();
                if (bundled != null && File.Exists(bundled))
                {
                    path = bundled;
                    // copy to local for editing
                    try { Directory.CreateDirectory(Path.GetDirectoryName(local)!); if (!File.Exists(local)) File.Copy(bundled, local, false); } catch {}
                }
            }
            if (path == null || !File.Exists(path)) return;
            var json = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("maps", out var mapsEl)) return;
            var dict = new Dictionary<string, Dictionary<string, CoordsRect>>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapProp in mapsEl.EnumerateObject())
            {
                var key = mapProp.Name; // e.g. "УЛК 3"
                var inner = new Dictionary<string, CoordsRect>(StringComparer.OrdinalIgnoreCase);
                foreach (var roomProp in mapProp.Value.EnumerateObject())
                {
                    try
                    {
                        var r = roomProp.Value;
                        var cr = new CoordsRect
                        {
                            x = r.GetProperty("x").GetDouble(),
                            y = r.GetProperty("y").GetDouble(),
                            w = r.GetProperty("w").GetDouble(),
                            h = r.GetProperty("h").GetDouble()
                        };
                        inner[roomProp.Name] = cr;
                    }
                    catch {}
                }
                dict[key] = inner;
            }
            _coords = dict;
            _coordsLoaded = true;
        }
        catch {}
    }

    public CoordsRect? GetCoords(string building, int floor, string roomRaw)
    {
        if (!_coordsLoaded) LoadCoords();
        var key = $"{building} {floor}";
        var roomKey = roomRaw?.Trim().TrimEnd(';').Replace("*","").Trim().ToLowerInvariant() ?? "";
        // also try without suffix like "а", "б" — keep as is, but also try stripped digits
        // Try exact, then digits only
        if (_coords.TryGetValue(key, out var inner))
        {
            if (inner.TryGetValue(roomKey, out var cr)) return cr;
            // try digits only
            var m = Regex.Match(roomKey, @"\d+");
            if (m.Success)
            {
                var digits = m.Value;
                if (inner.TryGetValue(digits, out var cr2)) return cr2;
                // also try with suffix like "326а" -> digits + suffix
                // already tried exact
            }
            // case-insensitive already, try lower
            foreach (var kv in inner)
            {
                if (kv.Key.Equals(roomKey, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            }
        }
        return null;
    }

    public void SaveCoords(string building, int floor, string roomRaw, double x, double y, double w, double h)
    {
        var key = $"{building} {floor}";
        var roomKey = roomRaw.Trim().TrimEnd(';').Replace("*","").Trim();
        if (string.IsNullOrWhiteSpace(roomKey)) return;
        if (!_coords.ContainsKey(key)) _coords[key] = new Dictionary<string, CoordsRect>(StringComparer.OrdinalIgnoreCase);
        _coords[key][roomKey] = new CoordsRect { x = x, y = y, w = w, h = h };
        // Save to local file
        try
        {
            var local = GetCoordsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            var toSave = new Dictionary<string, object>();
            toSave["version"] = 1;
            toSave["maps"] = _coords;
            var json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            File.WriteAllText(local, json, Encoding.UTF8);
        }
        catch {}
    }

    public MapInfo? Resolve(string classroomRaw)
    {
        if (string.IsNullOrWhiteSpace(classroomRaw)) return null;
        var raw = classroomRaw.Trim().TrimEnd(';').Trim();
        var lower = raw.ToLowerInvariant();
        if (lower.Contains("дистанционно"))
        {
            return new MapInfo
            {
                Building = "дистанционно",
                Floor = 0,
                Title = "Дистанционно",
                ClassroomRaw = classroomRaw,
                RoomRaw = raw,
                IsRemote = true,
                HasMap = false,
                Note = "Занятие дистанционно — карта не требуется"
            };
        }

        // Detect building
        string building;
        string roomPart = raw;
        bool hasStar = raw.Contains("*");

        if (raw.Contains("ВЦ") || raw.Contains("Вц") || raw.Contains("вц"))
        {
            building = "ВЦ";
            // room after ВЦ
            var mVc = Regex.Match(raw, @"ВЦ\s*(\d+)", RegexOptions.IgnoreCase);
            if (mVc.Success) roomPart = mVc.Groups[1].Value;
            else roomPart = Regex.Match(raw, @"\d+").Value ?? raw;
        }
        else if (hasStar)
        {
            building = "УЛК";
            // star = УЛК (per user correction 2026-09-01: кабинеты со звездочкой — УЛК)
            roomPart = raw.Replace("*", "").Trim();
            // if room like "507а" -> digits 507
            var m = Regex.Match(roomPart, @"\d+");
            if (m.Success) roomPart = m.Value;
        }
        else
        {
            // no star, not ВЦ -> ГК (main corpus)
            building = "ГК";
            var m = Regex.Match(raw, @"\d+");
            if (m.Success) roomPart = m.Value;
            else roomPart = raw;
        }

        // Extract floor from first digit of numeric part
        int floor = 1;
        var digitMatch = Regex.Match(roomPart, @"\d+");
        if (digitMatch.Success)
        {
            var digits = digitMatch.Value;
            if (digits.Length > 0 && char.IsDigit(digits[0]))
                floor = int.Parse(digits[0].ToString());
            if (floor < 1) floor = 1;
            if (floor > 5) floor = 5;
        }

        // Clamp floor to building max
        int maxFloor = building == "ГК" ? 4 : building == "ВЦ" ? 4 : 5;
        // ВЦ maps to ГК visuals: we use ГК map
        string mapBuilding = building == "ВЦ" ? "ГК" : building;
        if (mapBuilding == "ГК" && floor > 4) floor = 4; // show top floor with note
        if (mapBuilding == "УЛК" && floor > 5) floor = 5;

        string title;
        string url = "";
        bool hasMap = false;
        string note = "";

        if (building == "ВЦ")
        {
            title = $"ВЦ · {mapBuilding} {floor} этаж · ауд. {roomPart}";
            // ВЦ uses ГК map
            if (MapUrls.TryGetValue((mapBuilding, floor), out var u))
            {
                url = u;
                hasMap = true;
                note = "ВЦ — показать план ГК";
            }
        }
        else if (building == "ГК" || building == "УЛК")
        {
            title = $"{building} · {floor} этаж · ауд. {raw.Replace(";", "").Trim()}";
            if (MapUrls.TryGetValue((building, floor), out var u))
            {
                url = u;
                hasMap = true;
            }
            else
            {
                // fallback: try other building
                if (MapUrls.TryGetValue(("ГК", Math.Min(floor,4)), out var fallback))
                {
                    url = fallback;
                    hasMap = true;
                    note = $"Карта для {building} {floor} этажа — показан ближайший план";
                }
            }
        }
        else
        {
            title = $"{building} · {floor} этаж";
            hasMap = false;
        }

        var localPath = hasMap && !string.IsNullOrEmpty(url) ? GetLocalPathForUrl(url) : "";
        // parse room raw for display
        var roomRaw = roomPart;

        return new MapInfo
        {
            Building = building,
            Floor = floor,
            Title = title,
            Url = url,
            LocalPath = localPath,
            RoomRaw = roomRaw,
            ClassroomRaw = classroomRaw,
            HasMap = hasMap,
            IsRemote = false,
            Note = note
        };
    }

    public MapInfo? GetMapForLesson(Lesson lesson)
    {
        if (lesson == null) return null;
        return Resolve(lesson.ClassroomRaw);
    }

    // Ensure map file is cached locally (download if missing), fallback to bundled maps in app folder
    public async Task<string?> EnsureCachedAsync(MapInfo info, HttpClient? client = null)
    {
        if (info == null || !info.HasMap || string.IsNullOrEmpty(info.Url)) return null;
        var path = info.LocalPath;
        if (File.Exists(path) && new FileInfo(path).Length > 1000) return path;
        // Try bundled first (offline bundle in publish/maps)
        var bundled = GetBundledPathForUrl(info.Url);
        if (bundled != null && File.Exists(bundled))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(bundled, path, true);
                return path;
            }
            catch { return bundled; }
        }
        try
        {
            var http = client ?? Http;
            var bytes = await http.GetByteArrayAsync(info.Url);
            if (bytes.Length < 1000) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        catch
        {
            // final fallback to bundled even if copy failed earlier
            return bundled;
        }
    }

    public (int cached, int total, bool ready, string status) GetCacheStatus()
    {
        int total = MapUrls.Count;
        int cached = 0;
        foreach (var kv in MapUrls)
        {
            var local = GetLocalPathForUrl(kv.Value);
            var bundled = GetBundledPathForUrl(kv.Value);
            bool ok = (File.Exists(local) && new FileInfo(local).Length > 1000) || (bundled != null && File.Exists(bundled));
            if (ok) cached++;
        }
        bool ready = cached == total;
        string status = ready ? $"Офлайн: {cached}/{total} карт готово" : $"Кэш: {cached}/{total} — нажмите Скачать для офлайна";
        return (cached, total, ready, status);
    }

    public async Task EnsureAllMapsCachedAsync(HttpClient? client = null, IProgress<string>? progress = null, bool preferBundledFirst = true)
    {
        foreach (var kv in MapUrls)
        {
            var url = kv.Value;
            var path = GetLocalPathForUrl(url);
            if (File.Exists(path) && new FileInfo(path).Length > 1000) continue;
            // Try bundled first for offline without network
            if (preferBundledFirst)
            {
                var bundled = GetBundledPathForUrl(url);
                if (bundled != null && File.Exists(bundled))
                {
                    try
                    {
                        progress?.Report($"Копирование {kv.Key.building} {kv.Key.floor} из пакета...");
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.Copy(bundled, path, true);
                        progress?.Report($"Готово {Path.GetFileName(path)} (из пакета)");
                        continue;
                    }
                    catch (Exception ex) { progress?.Report($"Копия failed {kv.Key.building} {kv.Key.floor}: {ex.Message}"); }
                }
            }
            progress?.Report($"Downloading {kv.Key.building} {kv.Key.floor}...");
            try
            {
                var http = client ?? Http;
                var bytes = await http.GetByteArrayAsync(url);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, bytes);
                progress?.Report($"Cached {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                progress?.Report($"Failed {kv.Key.building} {kv.Key.floor}: {ex.Message}");
                // final fallback try bundled again
                var bundled2 = GetBundledPathForUrl(url);
                if (bundled2 != null && File.Exists(bundled2))
                {
                    try { File.Copy(bundled2, path, true); progress?.Report($"Восстановлено из пакета {Path.GetFileName(bundled2)}"); } catch {}
                }
            }
        }
    }

    // Find next lesson chronologically from now (today remaining, then tomorrow, then week)
    public (Lesson? lesson, DateTime date) GetNextLesson(string groupId, DateTime now)
    {
        // Try today
        for (int offset = 0; offset < NextLessonHorizonDays; offset++)
        {
            var date = now.Date.AddDays(offset);
            int dow = (int)date.DayOfWeek;
            if (dow == 0) dow = 7;
            if (dow == 7) continue; // Sunday no lessons
            var lessons = _schedule.GetSchedule(date, groupId);
            if (lessons.Count == 0) continue;
            // sort by TimeStart
            var sorted = lessons.OrderBy(l => l.TimeStart).ToList();
            foreach (var l in sorted)
            {
                if (offset == 0)
                {
                    // today: only future times
                    if (TimeSpan.TryParse(l.TimeStart, out var ts))
                    {
                        var lessonTime = date.Add(ts);
                        if (lessonTime > now) return (l, date);
                        // if lesson is ongoing? consider ongoing as next
                        if (TimeSpan.TryParse(l.TimeEnd, out var te))
                        {
                            var endTime = date.Add(te);
                            if (endTime > now) return (l, date);
                        }
                    }
                    else
                    {
                        return (l, date); // if no time, return first
                    }
                }
                else
                {
                    return (l, date);
                }
            }
        }
        return (null, now);
    }

    public List<MapInfo> GetAllMaps()
    {
        var list = new List<MapInfo>();
        foreach (var kv in MapUrls.OrderBy(k => k.Key.building).ThenBy(k => k.Key.floor))
        {
            list.Add(new MapInfo
            {
                Building = kv.Key.building,
                Floor = kv.Key.floor,
                Title = $"{kv.Key.building} {kv.Key.floor} этаж",
                Url = kv.Value,
                LocalPath = GetLocalPathForUrl(kv.Value),
                HasMap = true
            });
        }
        return list;
    }
}
