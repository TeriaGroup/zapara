using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ScheduleRemainingUx300Tests : UiTest
{
    [Fact]
    public async Task Remaining_today_hides_finished_lessons_without_changing_day_model()
    {
        using var db = TestDb.Create();
        var now = new DateTime(2026, 9, 14, 11, 0, 0);
        var vm = new ScheduleViewModel(db.Services, new ShellViewModel(db.Services), () => now);
        await vm.InitializeAsync();
        var total = vm.Lessons.Count;
        Assert.True(total >= 2);

        vm.RemainingToday = true;

        Assert.Equal(total, vm.Lessons.Count);
        Assert.All(vm.VisibleDayRows.OfType<LessonRowViewModel>(), row => Assert.False(row.IsPast));
        Assert.NotEmpty(vm.VisibleDayRows.OfType<LessonRowViewModel>());
    }

    [Fact]
    public async Task Day_workload_shows_first_start_last_end_and_real_breaks()
    {
        using var db = TestDb.Create();
        var vm = new ScheduleViewModel(db.Services, new ShellViewModel(db.Services),
            () => new DateTime(2026, 9, 14, 8, 0, 0));
        await vm.InitializeAsync();

        Assert.Contains("09:00", vm.WorkloadSpan);
        Assert.Contains("перерыв", vm.WorkloadSpan);
    }
}
