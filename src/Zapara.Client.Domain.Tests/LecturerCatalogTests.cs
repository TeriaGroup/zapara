using System.Xml;
using Vograph.Core.Models;
using Xunit;

namespace Zapara.Client.Domain.Tests;

public class LecturerCatalogTests
{
    private const string Xml = """
        <Timetable><Lecturer IdLecturer="1" LecturerName="Барт Елена Леонидовна" Kafedra="О6"><Days><Day Title="Понедельник"><LecturerLessons>
        <Lesson><WeekCode>0</WeekCode><Time>9:00 Нечетная</Time><Discipline>лек ВЫСШ. МАТЕМАТ</Discipline><Classroom>493*;</Classroom><Groups><Group><IdGroup>g</IdGroup><Number>А863С</Number></Group></Groups></Lesson>
        <Lesson><WeekCode>2</WeekCode><Time>10:50 Нечетная</Time><Discipline>пр ВЫСШ. МАТЕМАТ</Discipline><Classroom>дистанционно</Classroom></Lesson>
        </LecturerLessons></Day></Days></Lecturer><Lecturer IdLecturer="2" LecturerName="Барт Алексей Алексеевич" Kafedra="Р7"/></Timetable>
        """;

    [Fact]
    public void Parser_preserves_raw_subject_and_interprets_time_parity_room_and_groups()
    {
        var catalog = LecturerCatalog.Parse(Xml);
        Assert.Equal(2, catalog.Lecturers.Count);
        var l = Assert.Single(catalog.LessonsOf("1", parity: 1));
        Assert.Equal(("09:00", "10:35", 1, 1, "УЛК", "493", "ВЫСШ. МАТЕМАТ", "лек высш. математ"),
            (l.TimeStart, l.TimeEnd, l.DayOfWeek, l.Parity, l.BuildingRaw, l.RoomRaw, l.SubjectRaw, l.SubjectNormalized));
        Assert.Equal("А863С", Assert.Single(l.Groups).Number);
        Assert.Equal("дистанционно", Assert.Single(catalog.LessonsOf("1", parity: 1, invert: true)).RoomRaw);
    }

    [Fact]
    public void Search_matches_subject_department_and_full_name_but_only_mine_respects_initials()
    {
        var catalog = LecturerCatalog.Parse(Xml);
        Lesson[] mine = [new() { TeacherRaw = "Барт Е.Л.; Неизвестный А.А." }];
        Assert.Equal("1", Assert.Single(catalog.Search("матем", true, mine)).Id);
        Assert.Equal("2", Assert.Single(catalog.Search("р7")).Id);
        Assert.Equal("1", Assert.Single(catalog.Search("", true, mine)).Id);
        Assert.Empty(catalog.Search("", true));
        Assert.Equal(2, catalog.Search("Барт").Count);
    }

    [Theory]
    [InlineData("372*;", "УЛК", "372")]
    [InlineData("вц280;", "ВЦ", "вц280")]
    [InlineData("вц372*;", "ВЦ", "вц372")]
    [InlineData("ВЦ 372*;", "ВЦ", "372")]
    [InlineData("main 12;", "ГК", "12")]
    public void Star_is_ulk_and_computing_center_wins_over_a_star(string classroom, string building, string room)
    {
        var xml = "<Timetable><Lecturer IdLecturer=\"1\" LecturerName=\"Тест\" Kafedra=\"О6\"><Days><Day Title=\"Понедельник\"><LecturerLessons>"
            + "<Lesson><WeekCode>1</WeekCode><Time>9:00</Time><Discipline>лек ТЕСТ</Discipline><Classroom>"
            + classroom + "</Classroom></Lesson></LecturerLessons></Day></Days></Lecturer></Timetable>";
        var lesson = Assert.Single(LecturerCatalog.Parse(xml).Lessons);
        Assert.Equal((building, room), (lesson.BuildingRaw, lesson.RoomRaw));
    }

    [Theory]
    [InlineData("Барт Елена Леонидовна", "Барт Е.Л.", true)]
    [InlineData("Аббу Фадда Т.М.", "Аббу Фадда Т.М.", true)]
    [InlineData("Бартенев Е.Л.", "Барт Е.Л.", false)]
    [InlineData("Барт А.А.", "Барт Е.Л.", false)]
    public void Matching_keeps_compound_surnames_and_initials(string full, string shortName, bool expected) =>
        Assert.Equal(expected, LecturerCatalog.SameTeacher(full, shortName));

    [Fact]
    public void Parser_rejects_external_entities() => Assert.Throws<XmlException>(() => LecturerCatalog.Parse("<!DOCTYPE Timetable [<!ENTITY x SYSTEM 'file:///sensitive'>]><Timetable>&x;</Timetable>"));
}
