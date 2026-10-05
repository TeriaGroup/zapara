using Vograph.Core.Models;
using Vograph.Desktop.Features.Week;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class WeekRefreshDiffUx300Tests : UiTest
{
    [Fact]
    public async Task Same_real_week_reports_new_raw_slot_after_refresh_but_ignores_display_alias()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new WeekViewModel(db.Services, shell, () => new DateTime(2026, 9, 14));
        await vm.ReloadAsync();
        db.Services.Db.InsertLesson(new Lesson { GroupId = TestDb.MyGroupId, DayOfWeek = 1, Parity = 0,
            Index = 96, TimeStart = "17:00", TimeEnd = "18:30", SubjectRaw = "НОВЫЙ ПРЕДМЕТ",
            SubjectNormalized = "новый предмет", TypeRaw = "пр", TeacherRaw = "Петров", ClassroomRaw = "100;" });
        shell.RaiseScheduleChanged();
        await Waits.Until(() => vm.ShowRefreshDiff && vm.RefreshDiffText.Contains("НОВЫЙ ПРЕДМЕТ"), "week refresh diff");
        Assert.Contains("добавлено 1", vm.RefreshDiffText);
        db.Services.Overrides.AddOrUpdate("НОВЫЙ ПРЕДМЕТ", "global", "Отображаемое имя", "");
        shell.RaiseScheduleChanged();
        await Waits.Until(() => !vm.IsLoading, "alias event reload");
        Assert.False(vm.ShowRefreshDiff);
        Assert.Equal("", vm.RefreshDiffText);
    }
}
