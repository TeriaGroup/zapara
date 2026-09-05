using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ScheduleViewModelTests
{
    private static ScheduleViewModel Make(TestDb db, DateTime now)
    {
        var shell = new ShellViewModel(db.Services);
        var vm = new ScheduleViewModel(db.Services, shell, () => now);
        shell.Register(SectionKey.Schedule, () => vm);
        return vm;
    }

    [Fact]
    public async Task Initialize_Applies_Smart_Start_And_Segment()
    {
        using var db = TestDb.Create();
        var vm = Make(db, new DateTime(2026, 9, 7, 8, 0, 0));

        await vm.InitializeAsync();

        Assert.Equal(0, vm.DayOffset);
        Assert.Equal(1, vm.SegmentIndex);
        Assert.Equal("Сегодня", vm.Title);
        Assert.Equal(2, vm.Lessons.Count);
        Assert.False(vm.ShowGoToday);
        Assert.False(vm.IsEmpty);
        Assert.Equal("Матан", vm.Lessons[0].DisplayName);
        Assert.Equal("Барт Е.Л. · оригинал: ВЫСШ. МАТЕМАТ", vm.Lessons[0].TeacherLine);
        Assert.True(vm.Lessons[0].CanShowMap);
    }

    [Fact]
    public async Task Stepping_Days_Updates_Title_Segment_And_GoToday()
    {
        using var db = TestDb.Create();
        var vm = Make(db, new DateTime(2026, 9, 7, 8, 0, 0));
        await vm.InitializeAsync();

        vm.PrevDayCommand.Execute(null);
        await vm.ReloadAsync();
        Assert.Equal(-1, vm.DayOffset);
        Assert.Equal(0, vm.SegmentIndex);
        Assert.Equal("Вчера", vm.Title);
        Assert.True(vm.IsEmpty);
        Assert.Equal("Воскресенье — пар нет", vm.EmptyTitle);
        Assert.True(vm.ShowGoToday);

        vm.PrevDayCommand.Execute(null);
        await vm.ReloadAsync();
        Assert.Equal(-1, vm.SegmentIndex);           // out of the three-day segment
        Assert.Equal("Суббота", vm.Title);
        Assert.True(vm.Lessons[0].IsRemote);
        Assert.False(vm.Lessons[0].CanShowMap);

        vm.GoTodayCommand.Execute(null);
        await vm.ReloadAsync();
        Assert.Equal(0, vm.DayOffset);
        Assert.False(vm.ShowGoToday);

        vm.SegmentIndex = 2;                          // user clicks "Завтра"
        await vm.ReloadAsync();
        Assert.Equal(1, vm.DayOffset);
        Assert.Equal("Завтра", vm.Title);
        Assert.Equal("следующая пара — среда, 14:55", vm.EmptyHint);
    }

    [Fact]
    public async Task Language_Change_Relabels_Segments_And_Title()
    {
        using var db = TestDb.Create();
        var vm = Make(db, new DateTime(2026, 9, 7, 8, 0, 0));
        await vm.InitializeAsync();

        db.Services.Loc.SetLanguage("en");
        await vm.ReloadAsync();

        Assert.Equal(new[] { "Yesterday", "Today", "Tomorrow" }, vm.SegmentItems);
        Assert.Equal("Today", vm.Title);
        db.Services.Loc.SetLanguage("ru");
        await vm.ReloadAsync();
    }
}
