using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ScheduleLogicTests
{
    private static readonly Loc Ru = new(new I18nService("ru"));
    private static readonly DateTime Mon = new(2026, 9, 7);

    [Theory]
    [InlineData(-1, "Вчера")]
    [InlineData(0, "Сегодня")]
    [InlineData(1, "Завтра")]
    [InlineData(2, "Среда")]     // 09.09
    [InlineData(-2, "Суббота")]  // 05.09
    public void Title_Uses_Words_Near_Today_And_Weekday_Elsewhere(int offset, string expected) =>
        Assert.Equal(expected, DayTitles.Title(offset, Mon.AddDays(offset), Ru));

    [Fact]
    public void Subtitle_Joins_Date_Parity_Week_And_Count() =>
        Assert.Equal("Понедельник, 7 сентября · нечетная неделя · неделя 1 · 2 пары", DayTitles.Subtitle(Mon, isOdd: true, weekNumber: 1, lessonCount: 2, Ru));

    [Fact]
    public void Subtitle_Without_Lessons_Says_So() =>
        Assert.EndsWith("· Пар нет", DayTitles.Subtitle(Mon.AddDays(1), false, 2, 0, Ru));

    [Theory]
    [InlineData("лек", "лекция")]
    [InlineData("пр", "практика")]
    [InlineData("лаб", "лабораторная")]
    [InlineData("", "")]
    [InlineData("сем", "сем")]
    public void TypeLabel_Maps_Known_Types(string raw, string expected) => Assert.Equal(expected, DayTitles.TypeLabel(raw, Ru));

    [Theory]
    [InlineData("пр ОСН РОС ГОС", "пр", "ОСН РОС ГОС")]
    [InlineData("лек ВЫСШ. МАТЕМАТ", "лек", "ВЫСШ. МАТЕМАТ")]
    [InlineData("Матан", "лек", "Матан")]
    [InlineData("лек", "лек", "лек")]
    [InlineData("ПРАВО", "", "ПРАВО")]
    public void StripType_Removes_Only_The_Leading_Type_Token(string name, string type, string expected) =>
        Assert.Equal(expected, ScheduleComposer.StripType(name, type));

    [Fact]
    public void SmartStart_Today_While_Lessons_Remain_Otherwise_Tomorrow()
    {
        var lessons = new[] { new Lesson { TimeStart = "09:00", TimeEnd = "10:35" }, new Lesson { TimeStart = "12:40", TimeEnd = "14:15" } };
        Assert.Equal(0, SmartStart.InitialOffset(lessons, new TimeSpan(8, 0, 0)));
        Assert.Equal(0, SmartStart.InitialOffset(lessons, new TimeSpan(13, 0, 0)));
        Assert.Equal(1, SmartStart.InitialOffset(lessons, new TimeSpan(14, 16, 0)));
        Assert.Equal(1, SmartStart.InitialOffset(Array.Empty<Lesson>(), new TimeSpan(8, 0, 0)));
    }

    [Theory]
    [InlineData("done", 0, "сдано")]
    [InlineData("overdue", 0, "просрочено 07.09")]
    [InlineData("burning_urgent", 0, "горит сегодня")]
    [InlineData("burning", 0, "горит завтра")]
    [InlineData("far", 3, "срок 07.09 · через 3 пары")]
    [InlineData("approaching", 1, "срок 07.09 · через 1 пару")]
    [InlineData("far", 0, "срок 07.09")]
    public void Homework_Label_By_Status(string status, int lessonsUntil, string expected) =>
        Assert.Equal(expected, HomeworkLabels.Label(status, Mon, lessonsUntil, Ru));

    [Fact]
    public void Homework_Label_Without_Due_Date() => Assert.Equal("Срок: — (нет занятий)", HomeworkLabels.Label("pending", null, 0, Ru));

    [Theory]
    [InlineData("#F2A33C", 0)]
    [InlineData("#ff7a9c", 4)]
    [InlineData("#FF5AA9FF", 2)]   // with alpha
    [InlineData("#FF6CA5E0", 2)]   // legacy WPF blue → Friend3
    [InlineData("#98C379", 1)]     // legacy green → Friend2
    [InlineData("#FFE06C75", 4)]   // legacy red → Friend5 (pink)
    [InlineData("#C678DD", 3)]     // legacy violet → Friend4
    [InlineData("#FFF2C55C", 0)]   // legacy yellow → Friend1 (orange)
    [InlineData("nonsense", 0)]
    [InlineData(null, 0)]
    public void FriendPalette_Maps_New_Legacy_And_Unknown(string? hex, int expected) => Assert.Equal(expected, FriendPalette.IndexOf(hex));
}
