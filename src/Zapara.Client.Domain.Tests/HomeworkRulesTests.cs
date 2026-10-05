using Xunit;
using Zapara.Contracts.Sync;
using static Zapara.Client.Domain.Tests.ScheduleRulesTests;

namespace Zapara.Client.Domain.Tests;

public class HomeworkRulesTests
{
    [Fact]
    public void Sync_homework_converts_utc_to_local_creation_date_and_preserves_legacy_date()
    {
        var moscow = TimeZoneInfo.CreateCustomTimeZone("fixture-moscow", TimeSpan.FromHours(3), "Москва", "Москва");
        var value = new HomeworkValue("лек ВЫСШ. МАТ.", "лек высш. мат.", "Задание", 1, new DateTimeOffset(2026, 9, 13, 21, 30, 0, TimeSpan.Zero), null);
        var legacy = new HomeworkValue("лек ВЫСШ. МАТ.", "лек высш. мат.", "Задание", 1, value.CreatedAtUtc, new DateOnly(2026, 9, 13));
        Assert.Equal(new DateTime(2026, 9, 21), HomeworkRules.DueDate(Snapshot(Lesson()), "g", value, moscow));
        Assert.Equal(new DateTime(2026, 9, 14), HomeworkRules.DueDate(Snapshot(Lesson()), "g", legacy, moscow));
    }

    [Theory]
    [InlineData(-1, 21, 9)]
    [InlineData(1, 21, 9)]
    [InlineData(10, 23, 11)]
    [InlineData(11, 23, 11)]
    public void Due_date_clamps_one_to_ten_occurrences(int nth, int day, int month)
    {
        Assert.Equal(new DateTime(2026, month, day), HomeworkRules.DueDate(Snapshot(Lesson(), Lesson(start: "10:50")), "g", "лек ВЫСШ. МАТЕМАТ", new(2026, 9, 14, 1, 0, 0), nth));
    }

    [Theory]
    [InlineData(14, false, "overdue")]
    [InlineData(15, false, "burning_urgent")]
    [InlineData(16, false, "burning")]
    [InlineData(17, false, "approaching")]
    [InlineData(21, false, "far")]
    [InlineData(14, true, "done")]
    public void Status_uses_explicit_local_calendar_day(int dueDay, bool done, string status)
    {
        Assert.Equal(status, HomeworkRules.Status(Snapshot(Lesson(day: 3), Lesson(day: 4)), "g", "лек ВЫСШ. МАТ.", new(2026, 9, dueDay), done, new(2026, 9, 15, 23, 59, 0)));
    }

    [Fact]
    public void Missing_deadline_is_pending_and_completion_wins()
    {
        Assert.Equal("pending", HomeworkRules.Status(Snapshot(), "g", "мат", null, false, Start));
        Assert.Equal("done", HomeworkRules.Status(Snapshot(), "g", "мат", null, true, Start));
    }

    [Fact]
    public void Status_counts_individual_lessons_before_due_like_existing_windows_service()
    {
        var one = Snapshot(Lesson(day: 3));
        var two = Snapshot(Lesson(day: 3), Lesson(day: 3, start: "10:50"));
        Assert.Equal("approaching", HomeworkRules.Status(one, "g", "лек ВЫСШ. МАТ.", new(2026, 9, 21), false, new(2026, 9, 15)));
        Assert.Equal("far", HomeworkRules.Status(two, "g", "лек ВЫСШ. МАТ.", new(2026, 9, 21), false, new(2026, 9, 15)));
    }
}
