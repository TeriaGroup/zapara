using System.Text;
using System.Xml;
using Vograph.Core.Models;
using Vograph.Core.Services;

namespace Vograph.Timetable;

public class TimetableParser
{
    public const string DefaultUrl = "https://voenmeh.ru/wp-content/themes/Avada-Child-Theme-Voenmeh/_voenmeh_grafics/TimetableGroup50.xml";

    /// <summary>voenmeh.ru serves the XML as UTF-16LE with a BOM; archives came as UTF-8 with and without one.
    /// The single decoder for timetables, lecturers and the desktop refresher.</summary>
    public static string DecodeXml(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var utf8 = Encoding.UTF8.GetString(bytes);
        return utf8.Contains('\0') ? Encoding.Unicode.GetString(bytes) : utf8;
    }

    public (List<Group> groups, List<Lesson> lessons, DateTime periodStart, int weekCount, string periodTitle) Parse(string xml)
    {
        var doc = new XmlDocument { XmlResolver = null };
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        doc.Load(reader);
        var periodNode = doc.SelectSingleNode("/Timetable/Period");
        if (periodNode == null) throw new Exception("Period not found");
        var title = periodNode.Attributes!["Title"]?.Value ?? "";
        var sy = int.Parse(periodNode.Attributes!["StartYear"]?.Value ?? "2026");
        var sm = int.Parse(periodNode.Attributes!["StartMonth"]?.Value ?? "9");
        var sd = int.Parse(periodNode.Attributes!["StartDay"]?.Value ?? "1");
        var periodStart = new DateTime(sy, sm, sd);
        var weeksNode = doc.SelectSingleNode("/Timetable/Weeks");
        var weekCount = 2;
        if (weeksNode?.Attributes?["WeekCount"] != null)
            int.TryParse(weeksNode.Attributes["WeekCount"]!.Value, out weekCount);

        var groups = new List<Group>();
        var lessons = new List<Lesson>();

        var dayMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Понедельник"] = 1,
            ["Вторник"] = 2,
            ["Среда"] = 3,
            ["Четверг"] = 4,
            ["Пятница"] = 5,
            ["Суббота"] = 6,
            ["Воскресенье"] = 7
        };

        var groupNodes = doc.SelectNodes("/Timetable/Group");
        if (groupNodes != null)
        {
            foreach (XmlNode gn in groupNodes)
            {
                var id = gn.Attributes?["IdGroup"]?.Value ?? "";
                var name = gn.Attributes?["Number"]?.Value ?? "";
                if (string.IsNullOrEmpty(id)) continue;
                // Group without Days is just a list entry (no schedule yet)
                groups.Add(new Group { Id = id, Name = name, Url = DefaultUrl });

                var daysNode = gn.SelectSingleNode("Days");
                if (daysNode == null) continue;

                var dayNodes = daysNode.SelectNodes("Day");
                if (dayNodes == null) continue;
                foreach (XmlNode dayNode in dayNodes)
                {
                    var dayTitle = dayNode.Attributes?["Title"]?.Value ?? "";
                    int dayNum = dayMap.TryGetValue(dayTitle, out var v) ? v : 0;
                    if (dayNum == 0) continue;

                    var lessonNodes = dayNode.SelectNodes("GroupLessons/Lesson");
                    if (lessonNodes == null) continue;
                    // need to keep index per day+parity
                    var indexPerParity = new Dictionary<int, int>();
                    foreach (XmlNode ln in lessonNodes)
                    {
                        var wcStr = ln.SelectSingleNode("WeekCode")?.InnerText?.Trim() ?? "0";
                        int.TryParse(wcStr, out var parity);
                        if (parity < 0 || parity > 2) parity = 0;

                        var timeRaw = ln.SelectSingleNode("Time")?.InnerText?.Trim() ?? "";
                        var discRaw = ln.SelectSingleNode("Discipline")?.InnerText?.Trim() ?? "";
                        var classroomRaw = ln.SelectSingleNode("Classroom")?.InnerText?.Trim() ?? "";

                        // teacher raw: join ShortName
                        var lectNodes = ln.SelectNodes("Lecturers/Lecturer");
                        var teachers = new List<string>();
                        if (lectNodes != null)
                        {
                            foreach (XmlNode lec in lectNodes)
                            {
                                var sn = lec.SelectSingleNode("ShortName")?.InnerText?.Trim();
                                if (!string.IsNullOrEmpty(sn)) teachers.Add(sn);
                            }
                        }
                        var teacherRaw = string.Join("; ", teachers);

                        // Parse type and subject
                        string typeRaw = "";
                        string subjectRaw = discRaw;
                        if (!string.IsNullOrWhiteSpace(discRaw))
                        {
                            var parts = discRaw.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 2)
                            {
                                // Heuristic: first token is type if it's short (лек, пр, лаб, etc.)
                                var first = parts[0].ToLowerInvariant();
                                if (first is "лек" or "пр" or "лаб" or "конс" or "зач" or "экз" or "курс" or "практика")
                                {
                                    typeRaw = parts[0];
                                    subjectRaw = parts[1];
                                }
                                else
                                {
                                    // keep full as subject, type empty
                                    typeRaw = "";
                                    subjectRaw = discRaw;
                                }
                            }
                        }

                        // Parse timeStart/timeEnd
                        string timeStart = "";
                        string timeEnd = "";
                        if (!string.IsNullOrWhiteSpace(timeRaw))
                        {
                            // Time like "9:00 Нечетная" -> extract HH:mm
                            var m = System.Text.RegularExpressions.Regex.Match(timeRaw, @"(\d{1,2}:\d{2})");
                            if (m.Success) timeStart = m.Groups[1].Value.PadLeft(5, '0'); // ensure 09:00
                            // derive end +95 min
                            if (!string.IsNullOrEmpty(timeStart) && TimeSpan.TryParse(timeStart, out var ts))
                            {
                                var te = ts.Add(TimeSpan.FromMinutes(95));
                                timeEnd = te.ToString(@"hh\:mm");
                            }
                        }

                        // Parse room/building
                        string roomRaw = "";
                        string buildingRaw = "";
                        if (!string.IsNullOrWhiteSpace(classroomRaw))
                        {
                            var raw = classroomRaw.Trim().TrimEnd(';').Trim();
                            // split first token if building prefix like "ВЦ 282"
                            // If raw contains letters and digits, separate
                            // Simple: if raw contains space, building = before space, room = after
                            // Also handle "дистанционно"
                            if (raw.Equals("дистанционно", StringComparison.OrdinalIgnoreCase))
                            {
                                roomRaw = raw;
                                buildingRaw = "";
                            }
                            else
                            {
                                // remove * indicator
                                var clean = raw.Replace("*", "").Trim();
                                // check for building codes
                                var parts = clean.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 2 && parts[0].Any(char.IsLetter))
                                {
                                    buildingRaw = parts[0];
                                    roomRaw = string.Join(" ", parts.Skip(1)).TrimEnd(';');
                                }
                                else
                                {
                                    roomRaw = clean;
                                    buildingRaw = raw.Contains("*") ? "*" : "";
                                    // Keep building as "*" or empty? Spec says buildingRaw separate
                                    // if original had "*", building is main corpus
                                    if (raw.Contains("*")) buildingRaw = "main";
                                }
                            }
                        }

                        if (!indexPerParity.ContainsKey(parity)) indexPerParity[parity] = 0;
                        indexPerParity[parity]++;
                        int idx = indexPerParity[parity];

                        var lesson = new Lesson
                        {
                            GroupId = id,
                            DayOfWeek = dayNum,
                            Parity = parity,
                            Index = idx,
                            TimeStart = timeStart,
                            TimeEnd = timeEnd,
                            SubjectRaw = discRaw, // keep full discipline as spec says raw
                            SubjectNormalized = ParityService.NormalizeSubject(discRaw),
                            TeacherRaw = teacherRaw,
                            RoomRaw = roomRaw,
                            BuildingRaw = buildingRaw,
                            TypeRaw = typeRaw,
                            ClassroomRaw = classroomRaw
                        };
                        lessons.Add(lesson);
                    }
                }
            }
        }

        return (groups, lessons, periodStart, weekCount, title);
    }

}
