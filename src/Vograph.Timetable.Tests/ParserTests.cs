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
        Assert.Equal(("лек Математика", "лек математика", "лек", "Иванов И.И.", "493;", "493", ""),
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
