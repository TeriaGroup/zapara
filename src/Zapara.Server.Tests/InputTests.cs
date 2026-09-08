using System.Text;
using System.Xml.Linq;
using Vograph.Timetable;
using Xunit;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

public class InputTests
{
    internal const int Limit = 16 * 1024 * 1024;
    internal static string Xml => ContractTests.Fixture("valid-a.xml");
    internal static SourceDocument Source(string xml) => SourceDocument.Create(Encoding.UTF8.GetBytes(xml), SourceKind.File, TimeProvider.System);
    internal static TimetableInput Input => new(TimeProvider.System);

    [Theory]
    [InlineData("valid-a.xml", 2)]
    [InlineData("valid-b.xml", 1)]
    public void Accepts_smaller_snapshot_and_listed_empty_groups(string fixture, int count)
    {
        var source = Source(ContractTests.Fixture(fixture));
        var actual = Input.Validate(source);
        var (groups, lessons, start, weeks, title) = new TimetableParser().Parse(source.DecodedXml);
        var expected = SnapshotMapper.FromParsed(groups, lessons, start, weeks, title, source);
        Assert.Equal(count, actual.Groups.Length);
        Assert.Equal(expected.Period, actual.Period);
        Assert.Equal(expected.Groups.ToArray(), actual.Groups.ToArray());
        Assert.Equal(expected.Lessons.ToArray(), actual.Lessons.ToArray());
        Assert.Same(source, actual.Source);
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        foreach (var node in new[] { "Period", "Weeks", "DayTitle", "WeekCode", "Time", "Discipline" })
            yield return Case("missing " + node, d => d.Descendants(node).First().Remove());
        foreach (var attribute in new[] { "StartYear", "StartMonth", "StartDay", "Title" })
            yield return Case("missing period " + attribute, d => d.Root!.Element("Period")!.Attribute(attribute)!.Remove());
        foreach (var node in new[] { "Period", "Weeks", "Days", "Day", "GroupLessons", "Time", "Discipline", "Lecturers", "Classroom", "DayTitle", "WeekCode" })
            yield return Case("duplicate " + node, d => { var e = d.Descendants(node).First(); e.AddAfterSelf(new XElement(e)); });
        foreach (var value in new[] { "", " ", "3313", " x ", "x\tx", new string('x', 65) })
            yield return Case("id " + value.Length + value, d => d.Descendants("Group").Last().SetAttributeValue("IdGroup", value));
        yield return Case("missing id", d => d.Descendants("Group").Last().Attribute("IdGroup")!.Remove());
        foreach (var value in new[] { "", " ", new string('n', 129) })
            yield return Case("number " + value.Length, d => d.Descendants("Group").First().SetAttributeValue("Number", value));
        foreach (var value in new[] { "0", "3", "two", "" })
            yield return Case("weeks " + value, d => d.Root!.Element("Weeks")!.SetAttributeValue("WeekCount", value));
        yield return Case("missing count", d => d.Root!.Element("Weeks")!.Attribute("WeekCount")!.Remove());
        yield return Case("date", d => d.Root!.Element("Period")!.SetAttributeValue("StartDay", "32"));
        yield return Case("title empty", d => d.Root!.Element("Period")!.SetAttributeValue("Title", " "));
        yield return Case("title long", d => d.Root!.Element("Period")!.SetAttributeValue("Title", new string('a', 257)));
        foreach (var value in new[] { "-1", "3", "", "x", "1.0" })
            yield return Case("parity " + value, d => d.Descendants("WeekCode").First().Value = value);
        foreach (var value in new[] { "24:00", "9:60", "22:25", "09:00 junk", "09:00 Четная", "9:00 Обе недели", "9:00Нечетная", "9:0", "", new string(' ', 65) })
            yield return Case("time " + value, d => d.Descendants("Time").First().Value = value);
        foreach (var value in new[] { "", "Вторник", "Monday" })
            yield return Case("day title " + value, d => d.Descendants("DayTitle").First().Value = value);
        yield return Case("day", d => d.Descendants("Day").First().SetAttributeValue("Title", "Monday"));
        yield return Case("discipline empty", d => d.Descendants("Discipline").First().Value = " ");
        yield return Case("discipline long", d => d.Descendants("Discipline").First().Value = new string('a', 2049));
        yield return Case("joined teachers", d => d.Descendants("Lecturer").First().AddAfterSelf(
            new XElement("Lecturer", new XElement("ShortName", new string('a', 2048)))));
        yield return Case("no groups", d => d.Descendants("Group").Remove());
        yield return Case("all empty", d => d.Descendants("Days").Remove());
        yield return Case("hidden group", d => d.Root!.Add(new XElement("Metadata", new XElement("Group"))));
        yield return Case("hidden lesson", d => d.Root!.Add(new XElement("Metadata", new XElement("Lesson"))));
        yield return Case("misplaced lesson", d => { var e = d.Descendants("Lesson").First(); e.Remove(); d.Root!.Add(e); });
        yield return Case("namespace", d => d.Root!.Name = XName.Get("Timetable", "urn:bad"));
        yield return Case("namespace attr", d => d.Root!.SetAttributeValue(XName.Get("a", "urn:bad"), "x"));
        yield return Case("unknown large leaf", d => d.Root!.Add(new XElement("Metadata", new string('a', 2049))));
        yield return Case("unknown large attr", d => d.Root!.SetAttributeValue("meta", new string('a', 2049)));
        yield return new object[] { "DTD XXE", "<!DOCTYPE Timetable [<!ENTITY x SYSTEM 'file:///input-secret'>]>" + Xml[(Xml.IndexOf("<Timetable>", StringComparison.Ordinal))..].Replace("лек Математика", "&x;") };
        yield return new object[] { "malformed", "<Timetable>" };
        yield return new object[] { "wrong root", Xml.Replace("Timetable", "Other") };
    }

    private static object[] Case(string name, Action<XDocument> mutate)
    {
        var doc = XDocument.Parse(Xml);
        mutate(doc);
        return new object[] { name, doc.ToString() };
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void TT003_rejects_malformed_without_details(string name, string xml)
    {
        Assert.NotEmpty(name);
        var error = Assert.Throws<TimetableInputException>(() => Input.Validate(Source(xml)));
        Assert.Equal(FailureCode.SnapshotMalformed, error.FailureCode);
        Assert.Null(error.InnerException);
        Assert.Equal(new TimetableInputException(FailureCode.SnapshotMalformed).Message, error.Message);
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public void Depth_boundary(int depth, bool accepted)
    {
        var metadata = string.Concat(Enumerable.Repeat("<M>", depth)) + "x" + string.Concat(Enumerable.Repeat("</M>", depth));
        Check(Xml.Replace("</Timetable>", metadata + "</Timetable>"), accepted);
    }

    [Theory]
    [InlineData(5000, true)]
    [InlineData(5001, false)]
    public void Group_count_boundary(int count, bool accepted)
    {
        var doc = XDocument.Parse(Xml);
        doc.Root!.Elements("Group").Last().Remove();
        for (var i = 1; i < count; i++) doc.Root.Add(new XElement("Group", new XAttribute("IdGroup", "g" + i), new XAttribute("Number", "n")));
        Check(doc.ToString(SaveOptions.DisableFormatting), accepted);
    }

    [Theory]
    [InlineData(50000, true)]
    [InlineData(50001, false)]
    public void Lesson_count_boundary(int count, bool accepted)
    {
        var doc = XDocument.Parse(Xml);
        var container = doc.Descendants("GroupLessons").Single();
        container.RemoveNodes();
        var lesson = new XElement("Lesson", new XElement("DayTitle", "Понедельник"), new XElement("WeekCode", "0"), new XElement("Time", "9:00"), new XElement("Discipline", "x"));
        for (var i = 0; i < count; i++) container.Add(new XElement(lesson));
        Check(doc.ToString(SaveOptions.DisableFormatting), accepted);
    }

    [Fact]
    public void Exact_field_boundaries_and_time_parities_are_accepted()
    {
        foreach (var (parity, suffix) in new[] { (0, "Обе недели"), (1, "Нечетная"), (2, "Четная") })
        {
            var doc = XDocument.Parse(Xml);
            doc.Descendants("WeekCode").First().Value = parity.ToString();
            doc.Descendants("Time").First().Value = "22:24 " + suffix;
            doc.Descendants("Discipline").First().Value = new string('x', 2048);
            doc.Descendants("ShortName").First().Value = new string('x', 2048);
            doc.Descendants("Group").First().SetAttributeValue("IdGroup", new string('i', 64));
            doc.Descendants("Group").First().SetAttributeValue("Number", new string('n', 128));
            doc.Root!.Element("Period")!.SetAttributeValue("Title", new string('t', 256));
            Check(doc.ToString(), true);
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Caller_constructed_source_byte_boundary(int extra, bool accepted) => Check(Padded(Limit + extra), accepted);

    internal static string Padded(int bytes)
    {
        var xml = Xml;
        return xml.Replace("</Timetable>", new string(' ', bytes - Encoding.UTF8.GetByteCount(xml)) + "</Timetable>");
    }

    private static void Check(string xml, bool accepted)
    {
        if (accepted) Assert.NotEmpty(Input.Validate(Source(xml)).Lessons);
        else Assert.Equal(FailureCode.SnapshotMalformed, Assert.Throws<TimetableInputException>(() => Input.Validate(Source(xml))).FailureCode);
    }
}
