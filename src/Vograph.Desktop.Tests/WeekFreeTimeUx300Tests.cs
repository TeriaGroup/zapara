using Vograph.Desktop.Features.Week;
using Xunit;

namespace Vograph.Desktop.Tests;

public class WeekFreeTimeUx300Tests
{
    [Fact]
    public void Overlapping_lessons_do_not_create_fake_free_interval()
    {
        WeekRow[] lessons =
        [
            new("09:00", "Матан", "", "", TimeEnd: "10:30"),
            new("10:00", "Практика", "", "", TimeEnd: "11:00"),
            new("12:40", "Физика", "", "", TimeEnd: "14:10")
        ];

        var gap = Assert.Single(WeekFreeTime.Between(lessons));
        Assert.Contains("11:00–12:40", gap);
        Assert.Contains("1 ч 40 мин", gap);
    }
}
