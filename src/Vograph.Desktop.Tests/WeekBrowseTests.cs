using Vograph.Desktop.Features.Week;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class WeekBrowseTests
{
    private static readonly WeekDay Monday = new(1, "Понедельник", new DateTime(2026, 9, 14), false,
        [new WeekRow("09:00", "Матан", "Лекция", "493 ГК", "Математика", "Барт Е.Л.")]);
    private static readonly WeekDay Tuesday = new(2, "Вторник", new DateTime(2026, 9, 15), false, []);
    private static readonly WeekDay Wednesday = new(3, "Среда", new DateTime(2026, 9, 16), false,
        [new WeekRow("10:40", "Физика", "Практика", "301 УЛК", "Физика", "Иванов И.И.")]);

    [Fact]
    public void Search_matches_teacher_subject_and_room_and_preserves_day_order()
    {
        var days = new[] { Monday, Tuesday, Wednesday };

        Assert.Equal([1], WeekBrowse.Filter(days, "барт", false).Select(day => day.Dow));
        Assert.Equal([3], WeekBrowse.Filter(days, "301 улк", false).Select(day => day.Dow));
        Assert.Equal([1], WeekBrowse.Filter(days, "математика", false).Select(day => day.Dow));
        Assert.Equal([1, 3], WeekBrowse.Filter(days, "", true).Select(day => day.Dow));
    }

    [Fact]
    public void Search_does_not_discard_other_rows_of_matching_day()
    {
        var day = Monday with { Rows = [Monday.Rows[0], new WeekRow("12:40", "История", "Семинар", "111 ГК")] };

        var result = Assert.Single(WeekBrowse.Filter([day], "матан", false));
        Assert.Single(result.Rows);
        Assert.Equal("Матан", result.Rows[0].Name);
    }

    [Fact]
    public async Task Open_week_lesson_focuses_exact_time_in_schedule()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var week = new WeekViewModel(db.Services, shell, () => new DateTime(2026, 9, 14, 8, 0, 0));
        await week.ReloadAsync();
        var day = week.Days[0];
        var lesson = day.LessonRows[0];
        var schedule = shell.Section<ScheduleViewModel>(SectionKey.Schedule);
        LessonRowViewModel? focused = null;
        schedule.LessonFocusRequested += row => focused = row;

        lesson.OpenCommand.Execute(null);
        await Waits.Until(() => focused is not null, "week lesson focus");

        Assert.Equal(lesson.Time, focused!.TimeStart);
        Assert.Equal(day.Date, schedule.Date);
    }

    [Fact]
    public async Task Calendar_selection_opens_containing_week()
    {
        using var db = TestDb.Create();
        var vm = new WeekViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 14, 8, 0, 0));
        await vm.ReloadAsync();

        vm.CalendarWeekDate = new DateTime(2026, 9, 23);
        await Waits.Until(() => vm.Days.Count == 7 && vm.Days[0].Date == new DateTime(2026, 9, 21), "selected week");

        Assert.Contains("21", vm.WeekRange);
    }

    [Fact]
    public async Task Searching_a_collapsed_week_day_reveals_match_then_restores_collapse()
    {
        using var db = TestDb.Create();
        var vm = new WeekViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 14, 8, 0, 0));
        await vm.ReloadAsync();
        var day = vm.VisibleDays[0];
        day.ToggleCommand.Execute(null);
        Assert.True(vm.VisibleDays[0].IsCollapsed);

        vm.SearchQuery = "Матан";
        Assert.False(Assert.Single(vm.VisibleDays).IsCollapsed);
        vm.SearchQuery = "";
        Assert.True(vm.VisibleDays[0].IsCollapsed);
    }
}
