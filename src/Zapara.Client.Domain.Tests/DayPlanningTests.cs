using Zapara.Client.Domain;
using Xunit;

namespace Zapara.Client.Domain.Tests;

public sealed class DayPlanningTests
{
    [Fact]
    public void DateStripKeepsSelectionAcrossYearAndMidnight()
    {
        var today = new DateOnly(2026, 12, 31);
        var selected = new DateOnly(2027, 1, 2);
        Assert.Equal(today, DayPlanning.VisibleDates(today, selected, 7)[0]);
        Assert.Contains(selected, DayPlanning.VisibleDates(today.AddDays(1), selected, 7));
        var far = new DateOnly(2027, 3, 10);
        Assert.Contains(far, DayPlanning.VisibleDates(today, far, 7));
        Assert.Equal(7, DayPlanning.VisibleDates(today, far, 7).Distinct().Count());
    }

    [Fact]
    public void DateStripStaysInsideCalendarBounds()
    {
        Assert.Equal(DateOnly.MinValue, DayPlanning.VisibleDates(DateOnly.MinValue, DateOnly.MinValue, 7)[0]);
        Assert.Equal(DateOnly.MaxValue, DayPlanning.VisibleDates(DateOnly.MaxValue, DateOnly.MaxValue, 7)[^1]);
    }

    [Fact]
    public void OverlapAndContainedLessonsDoNotCreateNegativeOrFakeGaps()
    {
        DayInterval[] lessons = [new(Hm(9, 0), Hm(10, 35)), new(Hm(10, 0), Hm(11, 0)),
            new(Hm(10, 10), Hm(10, 20)), new(Hm(12, 40), Hm(14, 15))];
        var gap = Assert.Single(DayPlanning.FreeTime(lessons));
        Assert.Equal(Hm(11, 0), gap.Start);
        Assert.Equal(Hm(12, 40), gap.End);
        Assert.Equal(100, gap.Minutes);
    }

    [Fact]
    public void EveryPositiveBreakAppearsBetweenLessons()
    {
        DayInterval[] lessons = [new(Hm(9, 0), Hm(10, 0)), new(Hm(10, 29), Hm(11, 0)), new(Hm(11, 30), Hm(12, 0))];
        Assert.Equal(new[] { 29, 30 }, DayPlanning.FreeTime(lessons).Select(gap => gap.Minutes));
        Assert.Empty(DayPlanning.FreeTime([new(Hm(9, 0), Hm(10, 0))]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(60)]
    public void ShortAndLongBreaksUseTheSameRule(int minutes)
    {
        Assert.Equal(minutes, Assert.Single(DayPlanning.FreeTime([
            new(Hm(9, 0), Hm(10, 0)), new(Hm(10, 0).Add(TimeSpan.FromMinutes(minutes)), Hm(12, 0))
        ])).Minutes);
        Assert.Empty(DayPlanning.FreeTime([new(Hm(9, 0), Hm(10, 0)), new(Hm(10, 0), Hm(11, 0))]));
    }

    [Fact]
    public void DeadlineWindowUsesSelectedDayWithoutChangingRealOverdueStatus()
    {
        var day = new DateOnly(2026, 9, 28);
        Assert.True(DayPlanning.InDeadlineWindow(day, day.AddDays(2), false));
        Assert.False(DayPlanning.InDeadlineWindow(day, day.AddDays(3), true));
        Assert.False(DayPlanning.InDeadlineWindow(day, day.AddDays(-1), true));
        Assert.True(DayPlanning.InDeadlineWindow(day, null, true));
        Assert.False(DayPlanning.InDeadlineWindow(day, null, false));
    }

    [Fact]
    public void PriorityUsesCurrentThenUpcomingAndNeverClaimsPastDaysAreUpcoming()
    {
        var today = new DateOnly(2026, 9, 26);
        DayInterval[] lessons = [new(Hm(9, 0), Hm(10, 35)), new(Hm(12, 40), Hm(14, 15))];
        Assert.Equal(0, DayPlanning.PriorityIndex(lessons, today, today, Hm(9, 30)));
        Assert.Equal(1, DayPlanning.PriorityIndex(lessons, today, today, Hm(11, 0)));
        Assert.Equal(-1, DayPlanning.PriorityIndex(lessons, today, today, Hm(15, 0)));
        Assert.Equal(0, DayPlanning.PriorityIndex(lessons, today.AddDays(1), today, Hm(15, 0)));
        Assert.Equal(-1, DayPlanning.PriorityIndex(lessons, today.AddDays(-1), today, Hm(9, 30)));
    }

    private static TimeSpan Hm(int hours, int minutes) => new(hours, minutes, 0);
}
