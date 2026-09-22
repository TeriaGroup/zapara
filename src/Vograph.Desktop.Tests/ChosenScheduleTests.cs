using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Features.Summary;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ChosenScheduleTests
{
    private static Lesson Lesson(string teacher, int parity, int index) => new()
    {
        GroupId = "3313",
        DayOfWeek = 1,
        Parity = parity,
        Index = index,
        TimeStart = "09:00",
        TimeEnd = "10:35",
        SubjectRaw = "пр ИН. ЯЗ.",
        SubjectNormalized = "пр ИН. ЯЗ.",
        TeacherRaw = teacher,
        ClassroomRaw = index == 1 ? "101;" : "202;"
    };

    [Fact]
    public void Chosen_Subgroup_Skips_The_Other_Teachers_Monday()
    {
        var ivanov = Lesson("Иванов И.И.", 0, 1);
        var petrov = Lesson("Петров П.П.", 1, 2);
        var all = new List<Lesson> { ivanov, petrov };
        var stream = Assert.Single(SubgroupRules.Build(all).Streams);
        var choices = new Dictionary<string, string> { [stream.Id] = "петров п п" };
        var due = HomeworkCalendar.DueDate(all, choices, new DateTime(2026, 9, 1), 2, false, "пр ИН. ЯЗ.", new DateTime(2026, 9, 6), 1);
        Assert.Equal(new DateTime(2026, 9, 14), due);
        var settings = new Settings { MyGroupId = "3313", PeriodStart = "2026-09-01", WeekCount = 2 };
        Assert.Equal(new DateTime(2026, 9, 28), NextOccurrence.Find(all, choices, settings, "пр ИН. ЯЗ.", new DateTime(2026, 9, 14)));
        Assert.Equal(0, HomeworkLabels.LessonsUntil(all, choices, settings, "пр ИН. ЯЗ.", new DateTime(2026, 9, 14), new DateTime(2026, 9, 28)));
    }

    [Fact]
    public void Every_Week_Lesson_Stays_On_Both_Summary_Weeks_And_Teacher_Tabs()
    {
        var weekly = new Lesson { DayOfWeek = 1, Parity = 0, SubjectRaw = "обе" };
        var odd = new Lesson { DayOfWeek = 1, Parity = 1, SubjectRaw = "нечет" };
        var shown = SummaryComposer.ForUserParity(new[] { weekly, odd }, 1, false);
        Assert.Equal(2, shown.Count);
        Assert.Equal(new[] { weekly }, SummaryComposer.ForUserParity(new[] { weekly, odd }, 2, false));
        Assert.Equal(new[] { weekly }, SummaryComposer.ForUserParity(new[] { weekly, odd }, 1, true));
        Assert.True(ParityCodes.OnUserWeek(1, 0, false));
        Assert.True(ParityCodes.OnUserWeek(2, 0, true));
        Assert.False(ParityCodes.OnUserWeek(2, 1, false));
        Assert.Equal("summaryBothShort", ParityCodes.WeekLabelKey(0, true));
        Assert.Equal("evenShort", ParityCodes.WeekLabelKey(1, true));
    }
}
