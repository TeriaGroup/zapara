using Vograph.Core.Models;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class SubgroupRulesTests
{
    private static Lesson Lesson(int day, string time, string teacher, string subject = "пр ИН. ЯЗ.", string norm = "ин. яз.", int parity = 0, int index = 1, string room = "100;") => new()
    {
        GroupId = "3313",
        DayOfWeek = day,
        Parity = parity,
        Index = index,
        TimeStart = time,
        TimeEnd = "10:35",
        SubjectRaw = subject,
        SubjectNormalized = norm,
        TeacherRaw = teacher,
        ClassroomRaw = room
    };

    [Fact]
    public void Two_Teachers_At_One_Bell_Hide_The_Other_Row()
    {
        var ivanov = Lesson(1, "09:00", "Иванов И.И.", room: "101;");
        var petrov = Lesson(1, "09:00", "Петров П.П.", index: 2, room: "202;");
        var later = Lesson(3, "12:40", "Иванов И. И.", room: "101;");
        var laterOther = Lesson(3, "12:40", "Петров П.П.", index: 2, room: "202;");
        var all = new List<Lesson> { ivanov, petrov, later, laterOther };
        var index = SubgroupRules.Build(all);
        var stream = Assert.Single(index.Streams);
        Assert.Equal(new[] { "иванов и и", "петров п п" }, stream.Options.Select(option => option.Id));
        Assert.False(stream.Joined);
        var left = SubgroupRules.Visible(all, new Dictionary<string, string> { [stream.Id] = "иванов и и" });
        Assert.Equal(new[] { ivanov, later }, left);
        Assert.True(SubgroupRules.MarkOf(ivanov, new[] { ivanov, petrov }, index, new Dictionary<string, string>())!.ShowChooser);
        Assert.False(SubgroupRules.MarkOf(petrov, new[] { ivanov, petrov }, index, new Dictionary<string, string>())!.ShowChooser);
    }

    [Fact]
    public void One_Card_With_Two_Teachers_Stays()
    {
        var both = Lesson(1, "09:00", "Иванов И.И.; Петров П.П.");
        var index = SubgroupRules.Build(new[] { both });
        var stream = Assert.Single(index.Streams);
        Assert.True(stream.Joined);
        Assert.Equal(2, stream.Options.Count);
        var left = SubgroupRules.Visible(new[] { both }, new Dictionary<string, string> { [stream.Id] = stream.Options[0].Id });
        Assert.Equal(new[] { both }, left);
    }

    [Fact]
    public void Odd_And_Even_Teachers_Are_Not_Together()
    {
        var odd = Lesson(1, "09:00", "Иванов И.И.", parity: 1);
        var even = Lesson(1, "09:00", "Петров П.П.", parity: 2);
        Assert.Empty(SubgroupRules.Build(new[] { odd, even }).Streams);
    }

    [Fact]
    public void A_Weekly_Teacher_And_An_Alternating_Pair_Are_Two_Subgroups()
    {
        var always = Lesson(1, "09:00", "Иванов И.И.", parity: 0, room: "101;");
        var odd = Lesson(1, "09:00", "Петров П.П.", parity: 1, index: 2, room: "202;");
        var even = Lesson(1, "09:00", "Сидоров С.С.", parity: 2, index: 3, room: "303;");
        var thursday = Lesson(4, "09:00", "Иванов И.И.", parity: 0, room: "101;");
        var thursdayOdd = Lesson(4, "09:00", "Петров П.П.", parity: 1, index: 2, room: "202;");
        var thursdayEven = Lesson(4, "09:00", "Сидоров С.С.", parity: 2, index: 3, room: "303;");
        var all = new List<Lesson> { always, odd, even, thursday, thursdayOdd, thursdayEven };
        var stream = Assert.Single(SubgroupRules.Build(all).Streams);
        Assert.Equal(new[] { "иванов и и", "w:петров п п+сидоров с с" }, stream.Options.Select(option => option.Id));
        Assert.Equal("Петров П.П. · нечётная / Сидоров С.С. · чётная", stream.Options[1].Label);
        Assert.Equal(new[] { always, thursday }, SubgroupRules.Visible(all, new Dictionary<string, string> { [stream.Id] = "иванов и и" }));
        Assert.Equal(new[] { odd, even, thursdayOdd, thursdayEven }, SubgroupRules.Visible(all, new Dictionary<string, string> { [stream.Id] = stream.Options[1].Id }));
    }

    [Fact]
    public void Shared_Lecture_Stays_When_Only_The_Practice_Splits()
    {
        var lecture = Lesson(1, "09:00", "Сидоров С.С.", subject: "лек ФИЗИКА", norm: "физика", room: "1;");
        var labA = Lesson(1, "12:40", "Иванов И.И.", subject: "лаб ФИЗИКА", norm: "физика", index: 2, room: "2;");
        var labB = Lesson(1, "12:40", "Петров П.П.", subject: "лаб ФИЗИКА", norm: "физика", index: 3, room: "3;");
        var all = new List<Lesson> { lecture, labA, labB };
        var stream = Assert.Single(SubgroupRules.Build(all).Streams);
        var left = SubgroupRules.Visible(all, new Dictionary<string, string> { [stream.Id] = "иванов и и" });
        Assert.Equal(new[] { lecture, labA }, left);
    }
}
