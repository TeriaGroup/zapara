using System.Xml;
using Vograph.Timetable;

namespace Vograph.Core.Services;

/// <summary>The shared, IO-free parser used by desktop and browser clients.</summary>
public static class LecturerXmlParser
{
    public static (List<LecturerInfo> Lecturers, List<LecturerLesson> Lessons) Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var doc = new XmlDocument { XmlResolver = null };
        doc.Load(reader);
        var dayMap = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Понедельник"]=1, ["Вторник"]=2, ["Среда"]=3, ["Четверг"]=4, ["Пятница"]=5, ["Суббота"]=6, ["Воскресенье"]=7,
            // also handle garbled due to encoding? but doc should be correct UTF8
        };

        var lecturers = new List<LecturerInfo>();
        var lessons = new List<LecturerLesson>();

        var lecturerNodes = doc.SelectNodes("/Timetable/Lecturer");
        if (lecturerNodes == null)
        {
            return (lecturers, lessons);
        }
        foreach (XmlNode ln in lecturerNodes)
        {
            var id = ln.Attributes?["IdLecturer"]?.Value ?? "";
            var name = ln.Attributes?["LecturerName"]?.Value ?? "";
            var kaf = ln.Attributes?["Kafedra"]?.Value ?? "";
            if (string.IsNullOrEmpty(id)) continue;
            lecturers.Add(new LecturerInfo { Id = id, Name = name, Kafedra = kaf });

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
                    var wcStr = lnode.SelectSingleNode("WeekCode")?.InnerText?.Trim() ?? "";
                    var timeRaw = lnode.SelectSingleNode("Time")?.InnerText?.Trim() ?? "";
                    var parity = ParityService.ParseXmlParity(wcStr, timeRaw);
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
                    // Same rule as the group timetable: a star is УЛК, and ВЦ wins over a star.
                    var (roomRaw, buildingRaw) = TimetableParser.PlaceOf(classroomRaw);

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
                    lessons.Add(ll);
                }
            }
        }

        return (lecturers, lessons);
    }
}
