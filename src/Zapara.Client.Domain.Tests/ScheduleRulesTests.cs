using Vograph.Core.Models;
using Xunit;

namespace Zapara.Client.Domain.Tests;

public class ScheduleRulesTests
{
    internal static readonly DateTime Start = new(2026, 9, 1);
    internal static Lesson Lesson(int day = 1, int parity = 0, string subject = "лек ВЫСШ. МАТ.", string start = "09:00", string end = "10:35", string group = "g") => new()
    {
        GroupId = group, DayOfWeek = day, Parity = parity, SubjectRaw = subject,
        SubjectNormalized = subject.ToLowerInvariant(), TimeStart = start, TimeEnd = end
    };
    internal static ScheduleSnapshot Snapshot(params Lesson[] lessons) => new(Start, 2, [new Group { Id = "g", Name = "А863С" }], lessons);

    [Fact]
    public void Day_selects_group_and_parity_in_chronological_order_including_both_weeks()
    {
        var s = Snapshot(Lesson(parity: 2, start: "14:55"), Lesson(parity: 1), Lesson(start: "10:50"), Lesson(group: "other"), Lesson(day: 2));
        Assert.Equal(new[] { "10:50", "14:55" }, ScheduleRules.ForDate(s, "g", new(2026, 9, 7)).Select(l => l.TimeStart));
        Assert.Equal(new[] { "09:00", "10:50" }, ScheduleRules.ForDate(s, "g", new(2026, 9, 7), invert: true).Select(l => l.TimeStart));
        Assert.Empty(ScheduleRules.ForDate(s, "g", new(2026, 9, 6)));
    }

    [Theory]
    [InlineData(5, 10, 34, 5)]
    [InlineData(5, 10, 35, 7)]
    [InlineData(6, 9, 0, 7)]
    [InlineData(7, 10, 35, 8)]
    public void Smart_start_keeps_running_lesson_and_skips_sunday(int day, int hour, int minute, int expectedDay)
    {
        var s = Snapshot(Lesson(day: 6), Lesson());
        Assert.Equal(new DateTime(2026, 9, expectedDay), ScheduleRules.SmartStart(s, "g", new(2026, 9, day, hour, minute, 0)));
    }

    [Fact]
    public void Nth_occurrence_is_after_created_date_and_counts_a_double_lesson_once()
    {
        var s = Snapshot(Lesson(), Lesson(start: "10:50"), Lesson(day: 3));
        Assert.Equal(new DateTime(2026, 9, 16), ScheduleRules.NextOccurrence(s, "g", "лек ВЫСШ. МАТЕМАТ", new(2026, 9, 14), nth: 1));
        Assert.Equal(new DateTime(2026, 9, 21), ScheduleRules.NextOccurrence(s, "g", "лек ВЫСШ. МАТЕМАТ", new(2026, 9, 14), nth: 2));
        Assert.Equal("лек ВЫСШ. МАТ.", s.Lessons[0].SubjectRaw);
        Assert.Null(ScheduleRules.NextOccurrence(s, "g", "другой предмет", new(2026, 9, 14)));
    }

    [Fact]
    public void Search_preserves_snapshot_period_over_new_year_and_honors_limit()
    {
        var s = Snapshot(Lesson(parity: 1));
        Assert.Equal(new DateTime(2027, 1, 4), ScheduleRules.NextOccurrence(s, "g", "лек ВЫСШ. МАТ.", new(2026, 12, 31)));
        Assert.Null(ScheduleRules.NextOccurrence(s, "g", "лек ВЫСШ. МАТ.", new(2026, 12, 31), maxDays: 3));
    }
}
