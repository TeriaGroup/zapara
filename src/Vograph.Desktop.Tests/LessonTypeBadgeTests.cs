using Vograph.Desktop.Features.Schedule;
using Xunit;

namespace Vograph.Desktop.Tests;

public class LessonTypeBadgeTests
{
    [Theory]
    [InlineData("лек", LessonTypeBadgeKind.Lecture)]
    [InlineData("лекция", LessonTypeBadgeKind.Lecture)]
    [InlineData("пр", LessonTypeBadgeKind.Practice)]
    [InlineData("практика", LessonTypeBadgeKind.Practice)]
    [InlineData("лаб", LessonTypeBadgeKind.Lab)]
    [InlineData("лабораторная работа", LessonTypeBadgeKind.Lab)]
    [InlineData("конс", LessonTypeBadgeKind.Consult)]
    [InlineData("зачёт", LessonTypeBadgeKind.Credit)]
    [InlineData("экз", LessonTypeBadgeKind.Exam)]
    [InlineData("курс", LessonTypeBadgeKind.Course)]
    [InlineData("  ПРАКТИКА  ", LessonTypeBadgeKind.Practice)]
    [InlineData("семинар", LessonTypeBadgeKind.Unknown)]
    [InlineData("", LessonTypeBadgeKind.Unknown)]
    public void Known_and_unknown_raw_types_choose_the_expected_badge(string raw, LessonTypeBadgeKind expected) =>
        Assert.Equal(expected, LessonTypeBadge.KindOf(raw));

    [Fact]
    public void Missing_raw_type_is_neutral() =>
        Assert.Equal(LessonTypeBadgeKind.Unknown, LessonTypeBadge.KindOf(null));
}
