using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkNextLessonUx300Tests : UiTest
{
    [Fact]
    public async Task Homework_opens_next_matching_lesson_without_changing_group()
    {
        using var db = TestDb.Create();
        var now = new DateTime(2026, 9, 6, 12, 0, 0);
        var shell = new ShellViewModel(db.Services);
        var vm = new HomeworkViewModel(db.Services, shell, () => now);
        await vm.LoadAsync();
        var row = vm.Groups.SelectMany(group => group.Items).Single();
        var groupBefore = db.Services.Db.GetSettings().MyGroupId;

        await vm.OpenNextLessonAsync(row);
        var schedule = Assert.IsType<ScheduleViewModel>(shell.Current);
        await Waits.Until(() => schedule.Date == new DateTime(2026, 9, 7), "next math lesson");

        Assert.Equal(groupBefore, db.Services.Db.GetSettings().MyGroupId);
    }
}
