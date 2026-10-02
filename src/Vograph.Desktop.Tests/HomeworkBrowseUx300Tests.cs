using Vograph.Desktop.Features.Homeworks;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkBrowseUx300Tests
{
    private static readonly DateTime Today = new(2026, 9, 14);

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 2)]
    [InlineData(3, 3)]
    public void Deadline_filter_classifies_overdue_urgent_and_soon(int days, int expectedFilter)
    {
        for (var filter = 1; filter <= 3; filter++)
            Assert.Equal(filter == expectedFilter, HomeworkBrowse.MatchesDeadline(Today.AddDays(days), Today, filter));
    }

    [Fact]
    public void No_deadline_is_distinct_from_future_deadline()
    {
        Assert.True(HomeworkBrowse.MatchesDeadline(null, Today, 4));
        Assert.False(HomeworkBrowse.MatchesDeadline(Today.AddDays(20), Today, 4));
        Assert.True(HomeworkBrowse.MatchesDeadline(Today.AddDays(20), Today, 0));
    }

    [Fact]
    public void Shared_exact_deadline_distinguishes_past_and_future_hours_of_today()
    {
        var now = new DateTime(2026, 9, 14, 12, 0, 0);
        var past = new DateTimeOffset(now.AddHours(-1), TimeZoneInfo.Local.GetUtcOffset(now));
        var future = new DateTimeOffset(now.AddHours(1), TimeZoneInfo.Local.GetUtcOffset(now));
        Assert.True(HomeworkBrowse.MatchesSharedDeadline(past, now, 1));
        Assert.False(HomeworkBrowse.MatchesSharedDeadline(past, now, 2));
        Assert.False(HomeworkBrowse.MatchesSharedDeadline(future, now, 1));
        Assert.True(HomeworkBrowse.MatchesSharedDeadline(future, now, 2));
        Assert.True(HomeworkBrowse.MatchesDeadline(now.AddHours(-1), now, 2)); // personal due is date-only
        var late = new DateTime(2026, 9, 14, 23, 59, 0);
        var tomorrow = new DateTimeOffset(late.AddMinutes(2), TimeZoneInfo.Local.GetUtcOffset(late.AddMinutes(2)));
        Assert.True(HomeworkBrowse.MatchesSharedDeadline(tomorrow, late, 2));
        Assert.False(HomeworkBrowse.MatchesSharedDeadline(tomorrow, late, 1));
    }
}
