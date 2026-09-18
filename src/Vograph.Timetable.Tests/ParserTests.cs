using System.Text;
using System.Xml;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Timetable.Tests;

public sealed class ParserTests
{
    private static string Fixture() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "timetable-a.xml"));

    [Fact]
    public void TT001_Parse_fixture_a()
    {
        var result = new TimetableParser().Parse(Fixture());
        Assert.Equal(2, result.groups.Count);
        Assert.Equal(new DateTime(2026, 9, 1), result.periodStart);
        Assert.Equal(2, result.weekCount);
        Assert.Equal("ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г.", result.periodTitle);
        Assert.Equal(new[] { "3313", "9999" }, result.groups.Select(g => g.Id));
        Assert.Equal(new[] { "А863С", "Е452Б" }, result.groups.Select(g => g.Name));
        Assert.All(result.groups, g => Assert.Equal(TimetableParser.DefaultUrl, g.Url));
        var lesson = Assert.Single(result.lessons);
        Assert.Equal(0, lesson.Id);
        Assert.Equal(("3313", 1, 1, 1, "09:00", "10:35"),
            (lesson.GroupId, lesson.DayOfWeek, lesson.Parity, lesson.Index, lesson.TimeStart, lesson.TimeEnd));
        Assert.Equal(("лек Математика", "лек математика", "лек", "Иванов И.И.", "493;", "493", "ГК"),
            (lesson.SubjectRaw, lesson.SubjectNormalized, lesson.TypeRaw, lesson.TeacherRaw, lesson.ClassroomRaw, lesson.RoomRaw, lesson.BuildingRaw));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TT002_Decode_encodings(bool unicode, bool bom)
    {
        var xml = Fixture();
        var encoding = unicode ? Encoding.Unicode : Encoding.UTF8;
        var bytes = (bom ? encoding.GetPreamble() : Array.Empty<byte>()).Concat(encoding.GetBytes(xml)).ToArray();
        var decoded = TimetableParser.DecodeXml(bytes);
        Assert.Equal(xml, decoded);
        Assert.Equal("лек Математика", Assert.Single(new TimetableParser().Parse(decoded).lessons).SubjectRaw);
    }

    [Fact]
    public void Html_shell_is_not_a_timetable()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new TimetableParser().Parse("<!DOCTYPE html><html><body>spa</body></html>"));
        Assert.Equal(TimetableParser.NotTimetable, ex.Message);
    }

    [Theory]
    [InlineData("<!DOCTYPE Timetable>")]
    [InlineData("<!DOCTYPE Timetable [<!ENTITY title 'entity'>]>")]
    [InlineData("<!DOCTYPE Timetable SYSTEM 'file:///not-a-real-timetable.dtd'>")]
    public void TT002_Reject_dtd(string dtd)
    {
        var xml = Fixture();
        xml = xml.Insert(xml.IndexOf("<Timetable>", StringComparison.Ordinal), dtd);
        Assert.Throws<XmlException>(() => new TimetableParser().Parse(xml));
    }

    [Fact]
    public void TT004_Preserve_empty_group()
    {
        var result = new TimetableParser().Parse(Fixture());
        var empty = Assert.Single(result.groups, g => g.Id == "9999");
        Assert.Equal("Е452Б", empty.Name);
        Assert.DoesNotContain(result.lessons, l => l.GroupId == empty.Id);
    }

    [Theory]
    [InlineData("1", "9:00 Четная", 1)]
    [InlineData("2", "9:00 Нечетная", 2)]
    [InlineData("0", "9:00 Нечетная", 1)]
    [InlineData("", "10:50 Нечётная", 1)]
    [InlineData(null, "9:00 Четная", 2)]
    [InlineData("5", "12:40 Чётная", 2)]
    [InlineData("0", "9:00 Обе недели", 0)]
    [InlineData("", "9:00", 0)]
    public void ParseXmlParity_WeekCode_wins_else_Time_suffix(string? weekCode, string? timeRaw, int expected)
        => Assert.Equal(expected, ParityService.ParseXmlParity(weekCode, timeRaw));

    [Fact]
    public void Time_suffix_fills_missing_WeekCode()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Timetable>
              <Period Title="t" StartYear="2026" StartMonth="9" StartDay="1" />
              <Weeks WeekCount="2" />
              <Group Number="А863С" IdGroup="3313">
                <Days><Day Title="Понедельник"><GroupLessons>
                  <Lesson><WeekCode></WeekCode><Time>9:00 Нечетная</Time><Discipline>лек А</Discipline><Lecturers /><Classroom>1;</Classroom></Lesson>
                  <Lesson><WeekCode>0</WeekCode><Time>10:50 Четная</Time><Discipline>лек Б</Discipline><Lecturers /><Classroom>1;</Classroom></Lesson>
                  <Lesson><WeekCode>1</WeekCode><Time>12:40 Четная</Time><Discipline>лек В</Discipline><Lecturers /><Classroom>1;</Classroom></Lesson>
                </GroupLessons></Day></Days>
              </Group>
            </Timetable>
            """;
        var lessons = new TimetableParser().Parse(xml).lessons;
        Assert.Equal(new[] { 1, 2, 1 }, lessons.Select(l => l.Parity));
    }

    [Fact]
    public void SameSubject_matches_xml_and_json_spellings()
    {
        Assert.True(ParityService.SameSubject("лек ВЫСШ. МАТЕМАТ", "лек ВЫСШ. МАТ."));
        Assert.True(ParityService.SameSubject("пр ОСН РОС ГОС", "пр ОСН.РОС.ГОС"));
        Assert.False(ParityService.SameSubject("лек ВЫСШ. МАТЕМАТ", "пр ВЫСШ. МАТ."));
        Assert.False(ParityService.SameSubject("лек ФИЗИКА", "лек ФИЛОСОФИЯ"));
    }

    [Fact]
    public void Pure_assembly_owns_models_and_parity_without_native_dependencies()
    {
        var assembly = typeof(TimetableParser).Assembly;
        Assert.Equal(assembly, typeof(Group).Assembly);
        Assert.Equal(assembly, typeof(Lesson).Assembly);
        Assert.Equal(assembly, typeof(ParityService).Assembly);
        Assert.All(assembly.GetReferencedAssemblies(), reference =>
            Assert.True(reference.Name == "System.Private.CoreLib" || reference.Name!.StartsWith("System.", StringComparison.Ordinal)));
        const string url = TimetableParser.DefaultUrl;
        Assert.Equal("https://voenmeh.ru/wp-content/themes/Avada-Child-Theme-Voenmeh/_voenmeh_grafics/TimetableGroup50.xml", url);
    }
}
