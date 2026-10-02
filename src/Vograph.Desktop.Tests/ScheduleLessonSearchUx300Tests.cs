using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ScheduleLessonSearchUx300Tests : UiTest
{
    [Fact]
    public async Task Search_current_day_by_room_opens_exact_lesson_without_removing_others()
    {
        using var db = TestDb.Create();
        var vm = new ScheduleViewModel(db.Services, new ShellViewModel(db.Services),
            () => new DateTime(2026, 9, 14, 8, 0, 0));
        await vm.InitializeAsync();
        var total = vm.Lessons.Count;
        vm.LessonSearch = "493";
        var result = Assert.Single(vm.MatchingLessons);
        LessonRowViewModel? focused = null;
        vm.LessonFocusRequested += row => focused = row;

        vm.OpenSearchResultCommand.Execute(result);

        Assert.Same(result, focused);
        Assert.True(result.ShowDetails);
        Assert.Equal(total, vm.Lessons.Count);
    }

    [Fact]
    public async Task Jump_actions_target_current_lesson_and_deadline_panel()
    {
        using var db = TestDb.Create();
        var vm = new ScheduleViewModel(db.Services, new ShellViewModel(db.Services),
            () => new DateTime(2026, 9, 14, 8, 0, 0));
        await vm.InitializeAsync();
        LessonRowViewModel? focused = null;
        var deadlinesRequested = false;
        vm.LessonFocusRequested += row => focused = row;
        vm.DeadlineFocusRequested += () => deadlinesRequested = true;

        vm.JumpNearestLessonCommand.Execute(null);
        vm.JumpDeadlinesCommand.Execute(null);

        Assert.NotNull(focused);
        Assert.True(focused!.IsNext);
        Assert.True(deadlinesRequested);
    }
}
