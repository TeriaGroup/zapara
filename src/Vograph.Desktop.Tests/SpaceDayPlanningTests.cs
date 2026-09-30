using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Shell;
using Xunit;
namespace Vograph.Desktop.Tests;
public sealed class SpaceDayPlanningTests
{
    [Fact] public async Task Selected_absolute_day_survives_midnight_and_reactivation()
    {
        using var db=TestDb.Create();var now=new DateTime(2026,9,14,23,59,0);
        var shell=new ShellViewModel(db.Services);var vm=new ScheduleViewModel(db.Services,shell,()=>now);
        await vm.InitializeAsync();vm.SelectDate(new DateTime(2026,9,16));await vm.ReloadAsync();
        now=new DateTime(2026,9,15,0,1,0);await vm.ReloadAsync();
        Assert.Equal(new DateTime(2026,9,16),vm.Date);Assert.Equal(1,vm.SegmentIndex);Assert.Equal(1,vm.DayOffset);
        await vm.ActivateAsync();Assert.Equal(new DateTime(2026,9,16),vm.Date);vm.Detach();shell.Detach();
    }
    [Fact] public async Task Day_navigation_and_fast_buttons_include_empty_dates()
    {
        using var db=TestDb.Create();var shell=new ShellViewModel(db.Services);var vm=new ScheduleViewModel(db.Services,shell,()=>new DateTime(2026,9,12,12,0,0));
        await vm.InitializeAsync();vm.NextDayCommand.Execute(null);await vm.ReloadAsync();Assert.Equal(new DateTime(2026,9,13),vm.Date);Assert.True(vm.IsEmpty);
        vm.SegmentIndex=2;await vm.ReloadAsync();Assert.Equal(new DateTime(2026,9,14),vm.Date);Assert.Equal(7,vm.DateChoices.Count);vm.Detach();shell.Detach();
    }
    [Fact] public async Task Reload_reuses_lesson_objects_so_keyboard_focus_can_remain_on_actions()
    {
        using var db=TestDb.Create();var shell=new ShellViewModel(db.Services);var vm=new ScheduleViewModel(db.Services,shell,()=>new DateTime(2026,9,14,8,0,0));
        await vm.InitializeAsync();var first=vm.Lessons[0];await vm.ReloadAsync();Assert.Same(first,vm.Lessons[0]);Assert.Contains(first,vm.DayRows);vm.Detach();shell.Detach();
    }
    [Fact] public async Task Stable_lesson_rows_still_refresh_homework_readiness()
    {
        using var db=TestDb.Create();var shell=new ShellViewModel(db.Services);var vm=new ScheduleViewModel(db.Services,shell,()=>new DateTime(2026,9,14,8,0,0));
        await vm.InitializeAsync();var first=vm.Lessons[0];var homework=Assert.Single(first.Homework);db.Services.Homework.MarkDone(homework.Id,true);
        await vm.ReloadAsync();Assert.Same(first,vm.Lessons[0]);Assert.Same(homework,Assert.Single(vm.Lessons[0].Homework));Assert.True(homework.IsDone);vm.Detach();shell.Detach();
    }
    [Fact] public async Task Completed_today_keeps_a_visible_day_state_without_a_priority_lesson()
    {
        using var db=TestDb.Create();var shell=new ShellViewModel(db.Services);var vm=new ScheduleViewModel(db.Services,shell,()=>new DateTime(2026,9,14,23,0,0));
        await vm.InitializeAsync();Assert.False(vm.HasPriority);Assert.True(vm.ShowDayState);Assert.Equal("Пары закончились",vm.DayPriorityCaption);vm.Detach();shell.Detach();
    }

    [Fact] public async Task Every_break_is_in_the_timeline_even_if_the_old_preference_was_disabled()
    {
        using var db = TestDb.Create(false);
        db.Services.Prefs.ShowFreeTime = false;
        foreach (var (index, start, end, teacher) in new[] {
            (20, "18:00", "18:35", "Иванов"), (21, "18:45", "19:20", "Петров"), (22, "18:45", "19:20", "Сидоров") })
            db.Services.Db.InsertLesson(new() { GroupId = TestDb.MyGroupId, DayOfWeek = 1, Parity = 0, Index = index,
                TimeStart = start, TimeEnd = end, SubjectRaw = "пр Вечерний предмет", SubjectNormalized = "вечерний предмет", TeacherRaw = teacher });
        var shell = new ShellViewModel(db.Services);
        var vm = new ScheduleViewModel(db.Services, shell, () => new DateTime(2026, 9, 14, 8, 0, 0));
        try
        {
            await vm.InitializeAsync();
            var gap = Assert.Single(vm.DayRows.OfType<PlannerBreak>(), row => row.Label.Contains("18:35–18:45"));
            Assert.Equal("10 мин", gap.Duration);
            var next = vm.Lessons.First(row => row.TimeStart == "18:45");
            Assert.Equal(vm.DayRows.IndexOf(next) - 1, vm.DayRows.IndexOf(gap));
            await vm.ReloadAsync();
            Assert.Single(vm.DayRows.OfType<PlannerBreak>(), row => row.Label.Contains("18:35–18:45"));
        }
        finally { vm.Detach(); shell.Detach(); }
    }

}
