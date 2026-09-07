using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class DomainTests
{
    private static readonly Loc Ru = new(new I18nService("ru"));
    private static readonly Settings Fixture = new() { PeriodStart = "2026-09-01", WeekCount = 2 };

    [Fact]
    public void Period_Falls_Back_To_September_First_Of_The_Anchor_Year()
    {
        Assert.Equal((new DateTime(2026, 9, 1), 2), ParityCodes.Period(Fixture, new DateTime(2027, 3, 1)));
        Assert.Equal((new DateTime(2027, 9, 1), 2), ParityCodes.Period(new Settings { PeriodStart = null, WeekCount = 0 }, new DateTime(2027, 3, 1)));
        Assert.Equal((new DateTime(2026, 9, 1), 3), ParityCodes.Period(new Settings { PeriodStart = "2026-09-01", WeekCount = 3 }, new DateTime(2026, 9, 7)));
    }

    [Theory]
    [InlineData("2026-09-07", false, 1, true)]   // Mon 07.09 — odd
    [InlineData("2026-09-07", true, 2, false)]   // the same day inverted: XML code 2, shown as even
    [InlineData("2026-09-08", false, 2, false)]  // Tue 08.09 — even
    [InlineData("2026-09-08", true, 1, true)]
    [InlineData("2026-09-06", false, 1, true)]   // Sun 06.09 belongs to the Tue..Mon odd week
    public void WeekCode_And_IsOdd_Apply_The_Inversion(string date, bool invert, int code, bool odd)
    {
        var s = new Settings { PeriodStart = "2026-09-01", WeekCount = 2, ParityInvert = invert };
        Assert.Equal(code, ParityCodes.WeekCode(DateTime.Parse(date), s));
        Assert.Equal(odd, ParityCodes.IsOdd(DateTime.Parse(date), s));
    }

    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(2, false, 2)]
    [InlineData(1, true, 2)]
    [InlineData(2, true, 1)]
    public void ToXml_And_ToUser_Are_The_Same_Involution(int parity, bool invert, int expected)
    {
        Assert.Equal(expected, ParityCodes.ToXml(parity, invert));
        Assert.Equal(expected, ParityCodes.ToUser(parity, invert));
        Assert.Equal(parity, ParityCodes.ToUser(ParityCodes.ToXml(parity, invert), invert));
    }

    [Fact]
    public void Day_Keys_Follow_Core_Numbering()
    {
        Assert.Equal(new[] { "mon", "tue", "wed", "thu", "fri", "sat", "sun" }, Enumerable.Range(1, 7).Select(DayNames.Key));
        Assert.Equal("monShort", DayNames.ShortKey(1));
        Assert.Equal("Понедельник", Ru.T(DayNames.Key(1)));
        Assert.Equal("Пн", Ru.T(DayNames.ShortKey(1)));
        Assert.Equal("mon", DayNames.Key(0));   // clamped, never throws
        Assert.Equal("sun", DayNames.Key(9));
    }

    [Theory]
    [InlineData("526*; ", "526")]
    [InlineData("493;", "493")]
    [InlineData("ВЦ 280; ", "ВЦ 280")]
    [InlineData("326а*", "326а")]
    [InlineData("дистанционно", "дистанционно")]
    public void CleanRoom_Strips_The_Star_And_The_Separator(string raw, string expected) =>
        Assert.Equal(expected, LessonText.CleanRoom(raw));

    [Theory]
    [InlineData("лек ВЫСШ. МАТЕМАТ", "лек", "ВЫСШ. МАТЕМАТ")]
    [InlineData("Матан", "лек", "Матан")]
    [InlineData("пр ОСН РОС ГОС", "пр", "ОСН РОС ГОС")]
    [InlineData("практика", "практика", "практика")]
    [InlineData("лекарство от скуки", "лек", "лекарство от скуки")]
    public void StripType_Removes_Only_The_Leading_Type_Token(string name, string type, string expected) =>
        Assert.Equal(expected, LessonText.StripType(name, type));

    [Fact]
    public void RoomParts_Formats_Building_Remote_And_Vc()
    {
        using var db = TestDb.Create();
        var maps = db.Services.Maps;
        Assert.Equal(("493", "ГК", false), LessonText.RoomParts(new Lesson { RoomRaw = "493", ClassroomRaw = "493;" }, maps.Resolve("493;"), Ru));
        Assert.Equal(("526", "УЛК", false), LessonText.RoomParts(new Lesson { RoomRaw = "526", ClassroomRaw = "526*;" }, maps.Resolve("526*;"), Ru));
        Assert.Equal(("ВЦ 280", "ГК", false), LessonText.RoomParts(new Lesson { RoomRaw = "280", ClassroomRaw = "ВЦ 280;" }, maps.Resolve("ВЦ 280;"), Ru));
        Assert.Equal(("дистанционно", null, true), LessonText.RoomParts(new Lesson { RoomRaw = "дистанционно", ClassroomRaw = "дистанционно" }, maps.Resolve("дистанционно"), Ru));
        Assert.Equal(("—", null, false), LessonText.RoomParts(new Lesson { RoomRaw = "", ClassroomRaw = "" }, null, Ru));
    }
}
