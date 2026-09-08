using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Zapara.Server.Timetable;

internal static class TimetableXmlValidation
{
    private static readonly string[] Days = ["Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота", "Воскресенье"];
    private static readonly Regex TimePattern = new(@"\A([0-9]{1,2}):([0-9]{2})(?:\s+(Нечетная|Четная|Обе недели))?\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static void Require(bool condition)
    {
        if (!condition) throw new TimetableInputException(FailureCode.SnapshotMalformed);
    }

    internal static (int Groups, int Lessons) Validate(SourceDocument source)
    {
        Require(source.Bytes.Length is > 0 and <= TimetableInput.MaxBytes);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = TimetableInput.MaxBytes
        };
        var groups = 0;
        var lessons = 0;
        var path = new List<string>();
        // Bound depth/counts and reject namespaces before allocating a tree or invoking the lenient parser.
        using (var text = new StringReader(source.DecodedXml))
        using (var reader = XmlReader.Create(text, settings))
        {
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                Require(reader.Depth <= 32 && reader.NamespaceURI.Length == 0 && reader.Name.Length <= 2048);
                if (path.Count > reader.Depth) path.RemoveRange(reader.Depth, path.Count - reader.Depth);
                path.Add(reader.Name);
                var location = string.Join("/", path);
                if (reader.Depth == 0) Require(reader.Name == "Timetable");
                if (reader.Name == "Group") Require(location == "Timetable/Group" && ++groups <= 5000);
                if (reader.Name == "Lesson") Require(location == "Timetable/Group/Days/Day/GroupLessons/Lesson" && ++lessons <= 50000);
                if (reader.HasAttributes)
                {
                    while (reader.MoveToNextAttribute())
                        Require(reader.NamespaceURI.Length == 0 && reader.Prefix.Length == 0 && reader.Name != "xmlns"
                            && reader.Name.Length <= 2048 && reader.Value.Length <= FieldLimit(reader.Name));
                    reader.MoveToElement();
                }
            }
        }
        Require(groups > 0 && lessons > 0);
        using var input = new StringReader(source.DecodedXml);
        using var secureReader = XmlReader.Create(input, settings);
        var doc = XDocument.Load(secureReader);
        var root = doc.Root!;
        foreach (var element in root.DescendantsAndSelf())
        {
            if (!element.HasElements) Require(element.Value.Length <= FieldLimit(element.Name.LocalName));
        }
        var period = Single(root, "Period");
        RequiredAttribute(period, "Title");
        _ = new DateOnly(Integer(RequiredAttribute(period, "StartYear")), Integer(RequiredAttribute(period, "StartMonth")), Integer(RequiredAttribute(period, "StartDay")));
        Require(Integer(RequiredAttribute(Single(root, "Weeks"), "WeekCount")) == 2);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in root.Elements("Group"))
        {
            var id = RequiredAttribute(group, "IdGroup");
            Require(id.Length <= 64 && id == id.Trim() && !id.Any(char.IsControl) && ids.Add(id));
            RequiredAttribute(group, "Number");
            Require(group.Elements("Days").Count() <= 1);
            var days = group.Element("Days");
            if (days is null) continue;
            var seenDays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var day in days.Elements("Day"))
            {
                var title = RequiredAttribute(day, "Title");
                Require(Days.Contains(title, StringComparer.OrdinalIgnoreCase) && seenDays.Add(title));
                Require(day.Elements("GroupLessons").Count() <= 1);
                foreach (var lesson in day.Element("GroupLessons")?.Elements("Lesson") ?? []) ValidateLesson(lesson, title);
            }
        }
        return (groups, lessons);
    }

    private static int FieldLimit(string name) => name switch
    {
        "IdGroup" => 64,
        "Number" => 128,
        "Title" or "DayTitle" => 256,
        "Time" => 64,
        _ => 2048
    };

    private static XElement Single(XElement parent, string name)
    {
        var elements = parent.Elements(name).Take(2).ToArray();
        Require(elements.Length == 1);
        return elements[0];
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        var value = (string?)element.Attribute(name);
        Require(!string.IsNullOrWhiteSpace(value));
        return value!;
    }

    private static int Integer(string value)
    {
        Require(int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number));
        return number;
    }

    private static string Field(XElement lesson, string name)
    {
        var field = Single(lesson, name);
        Require(!field.HasElements && !string.IsNullOrWhiteSpace(field.Value));
        return field.Value.Trim();
    }

    private static void ValidateLesson(XElement lesson, string day)
    {
        Require(lesson.Elements().GroupBy(element => element.Name).All(group => group.Count() == 1));
        Require(string.Equals(Field(lesson, "DayTitle"), day, StringComparison.OrdinalIgnoreCase));
        var parity = Integer(Field(lesson, "WeekCode"));
        Require(parity is >= 0 and <= 2);
        ValidateTime(Field(lesson, "Time"), parity);
        Field(lesson, "Discipline");
        var classroom = lesson.Element("Classroom");
        Require(classroom is null || !classroom.HasElements);
        foreach (var lecturer in lesson.Element("Lecturers")?.Elements("Lecturer") ?? [])
        {
            Require(lecturer.Elements().GroupBy(element => element.Name).All(group => group.Count() == 1));
            Require(lecturer.Elements().All(element => !element.HasElements));
        }
        var teachers = lesson.Element("Lecturers")?.Elements("Lecturer")
            .Select(lecturer => lecturer.Element("ShortName")?.Value.Trim()).Where(name => !string.IsNullOrEmpty(name)) ?? [];
        Require(string.Join("; ", teachers).Length <= 2048);
    }

    internal static void ValidateTime(string value, int parity)
    {
        Require(value.Length <= 64);
        var match = TimePattern.Match(value);
        Require(match.Success);
        var hour = Integer(match.Groups[1].Value);
        var minute = Integer(match.Groups[2].Value);
        Require(hour <= 23 && minute <= 59 && hour * 60 + minute + 95 < 1440);
        var suffix = match.Groups[3].Value;
        Require(suffix.Length == 0 || suffix == (parity switch { 0 => "Обе недели", 1 => "Нечетная", 2 => "Четная", _ => "!" }));
    }
}
