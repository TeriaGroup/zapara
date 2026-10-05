using Vograph.Core.Models;
using Zapara.Client.Domain;
using Zapara.Contracts.Sync;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserScheduleViewTests
{
    private static readonly DateTime Monday = new(2026, 9, 21);
    private static readonly Lesson First = new() { GroupId = "g", DayOfWeek = 1, Index = 1, SubjectRaw = "лек Математика", SubjectNormalized = "лек математика", TimeStart = "09:00", TimeEnd = "10:35", RoomRaw = "301", BuildingRaw = "ГК" };
    private static readonly Lesson Second = new() { GroupId = "g", DayOfWeek = 1, Index = 2, SubjectRaw = "Физика", SubjectNormalized = "физика", TimeStart = "11:00", TimeEnd = "12:35" };
    private static ScheduleSnapshot Snapshot => new(Monday, 2, [], [First, Second]);

    [Fact]
    public void Homework_subject_options_distinguish_lecture_and_practice_without_changing_identity()
    {
        var lecture = new Lesson { GroupId = "g", SubjectRaw = "лек РАЗР.МОБ.ПРИЛ", TypeRaw = "лек" };
        var practice = new Lesson { GroupId = "g", SubjectRaw = "пр РАЗР.МОБ.ПРИЛ", TypeRaw = "пр" };
        Assert.Equal("РАЗР.МОБ.ПРИЛ · лек", Zapara.Web.Pages.Homework.SubjectOptionTitle(lecture.SubjectRaw, "РАЗР.МОБ.ПРИЛ", [lecture, practice], "g"));
        Assert.Equal("РАЗР.МОБ.ПРИЛ · пр", Zapara.Web.Pages.Homework.SubjectOptionTitle(practice.SubjectRaw, "РАЗР.МОБ.ПРИЛ", [lecture, practice], "g"));
        Assert.Equal("Высшая математика", Zapara.Web.Pages.Homework.SubjectOptionTitle("Высшая математика", "Высшая математика", [], "g"));
    }

    [Fact]
    public void Ready_at_mount_runs_smart_start_without_waiting_for_a_changed_event()
    {
        var view = new BrowserScheduleViewState();
        Assert.Equal(new DateTime(2026, 9, 22), view.Open("a", "g", Snapshot, Monday.AddHours(19), false));
    }

    [Fact]
    public void Explicit_query_wins_over_saved_day_and_invalid_query_does_not_reset_it()
    {
        var view = new BrowserScheduleViewState();
        view.Select("a", "g", Monday.AddDays(3));
        Assert.Equal(Monday.AddDays(7), view.Open("a", "g", Snapshot, Monday, false, "2026-09-28"));
        Assert.Equal(Monday.AddDays(7), view.Open("a", "g", Snapshot, Monday, false, "2026-02-31"));
        Assert.False(BrowserScheduleViewState.TryDate("2026-9-28", out _));
        Assert.False(BrowserScheduleViewState.TryDate("9999-12-31", out _));
    }

    [Fact]
    public void Navigation_restores_only_the_same_owner_and_group()
    {
        var view = new BrowserScheduleViewState();
        view.Select("a", "g", Monday.AddDays(3));
        Assert.Equal(Monday.AddDays(3), view.Open("a", "g", Snapshot, Monday.AddHours(9), false));
        Assert.Equal(Monday, view.Open("b", "g", Snapshot, Monday.AddHours(9), false));
        Assert.Equal(Monday.AddDays(1), view.Open("a", "other", Snapshot, Monday.AddHours(9), false));
        Assert.Equal(Monday.AddDays(3), view.Open("a", "g", Snapshot, Monday.AddHours(9), false));
    }

    [Fact]
    public void Missing_schedule_does_not_mark_smart_start_as_finished()
    {
        var view = new BrowserScheduleViewState();
        Assert.Equal(Monday, view.Open("a", "g", null, Monday.AddHours(19), false));
        Assert.Equal(Monday.AddDays(1), view.Open("a", "g", Snapshot, Monday.AddHours(19), false));
    }

    [Theory]
    [InlineData(8, 0, "next", "")]
    [InlineData(9, 0, "current", "next")]
    [InlineData(10, 35, "past", "next")]
    [InlineData(11, 0, "past", "current")]
    [InlineData(12, 35, "past", "past")]
    public void Clock_tick_moves_current_and_next_markers_at_exact_boundaries(int hours, int minutes, string first, string second)
    {
        Assert.Equal(new[] { first, second }, BrowserSchedulePresentation.Timings([First, Second], Monday, Monday.AddHours(hours).AddMinutes(minutes)));
        Assert.Equal(new[] { "", "" }, BrowserSchedulePresentation.Timings([First, Second], Monday.AddDays(7), Monday.AddHours(hours)));
    }

    [Fact]
    public void Homework_indicators_match_subjects_completion_and_real_today_not_the_viewed_day()
    {
        var urgent = Guid.NewGuid(); var done = Guid.NewGuid();
        var task = new HomeworkValue("лек Математика", "лек математика", "Задача", 1, new DateTimeOffset(Monday.AddDays(-7), TimeSpan.Zero), DateOnly.FromDateTime(Monday.AddDays(-7)));
        var rows = BrowserSchedulePresentation.Homework(Snapshot, "g", First, Monday,
            [new(urgent, 1, task), new(done, 1, task), new(Guid.NewGuid(), 1, new HomeworkValue("Физика", "физика", "Другое задание", 1, task.CreatedAtUtc, task.LegacyCreatedLocalDate))],
            [new(done, 1, new CompletionValue(true, DateTimeOffset.UtcNow))], false);
        Assert.Equal(2, rows.Count);
        Assert.Equal(urgent, rows[0].Id); Assert.Equal("burning_urgent", rows[0].Status); Assert.Equal(Monday, rows[0].Due);
        Assert.Equal(done, rows[1].Id); Assert.Equal("done", rows[1].Status);
    }

    [Fact]
    public void Friends_require_loaded_group_even_if_catalog_contains_matching_lessons()
    {
        var friendLesson = new Lesson { GroupId = "friend", DayOfWeek = 1, TimeStart = "09:00", TimeEnd = "10:35", RoomRaw = "301", BuildingRaw = "ГК" };
        var snapshot = Snapshot with { Lessons = [First, friendLesson] };
        var friend = new FriendValue("friend", "А102", "Маша", 2, true);
        var unavailable = BrowserSchedulePresentation.Friends(snapshot, First, Monday, [friend], _ => false, 25, true, false);
        Assert.False(Assert.Single(unavailable).HasCompatibleSchedule);
        Assert.False(unavailable[0].Present);
        Assert.Empty(BrowserSchedulePresentation.Friends(snapshot, First, Monday, [friend], _ => false, 25, false, false));
        var loaded = BrowserSchedulePresentation.Friends(snapshot, First, Monday, [friend], _ => true, 25, false, false);
        Assert.True(Assert.Single(loaded).Present); Assert.Equal(100, loaded[0].Score);
    }
}
