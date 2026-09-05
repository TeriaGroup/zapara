using Vograph.Core.Services;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Features.States;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class GroupCardTests
{
    private static readonly Loc Ru = new(new I18nService("ru"));
    private static readonly DateTime Now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("garbage", null, false)]
    [InlineData("2026-09-11T10:00:00.0000000Z", null, false)]                 // 1 day: fresh
    [InlineData("2026-09-08T10:00:00.0000000Z", "обновлено 08.09", false)]   // 4 days: chip
    [InlineData("2026-09-01T10:00:00.0000000Z", "обновлено 01.09", true)]    // 11 days: warn
    public void Stale_Chip_After_Three_Days_Warn_After_Seven(string? fetched, string? text, bool warn)
    {
        var (t, w) = GroupCardLogic.Stale(fetched, Now, Ru);
        Assert.Equal(text, t);
        Assert.Equal(warn, w);
    }

    [Fact]
    public async Task Picking_Another_Group_Saves_And_Notifies()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var changed = false;
        shell.GroupChanged += () => changed = true;

        var task = shell.OpenGroupPickerCommand.ExecuteAsync(null);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (shell.Dialogs.Current is not GroupPickerDialogViewModel && sw.ElapsedMilliseconds < 2000)
            await Task.Delay(10, TestContext.Current.CancellationToken); // xUnit1051: the token keeps the poll cancellable
        var dlg = Assert.IsType<GroupPickerDialogViewModel>(shell.Dialogs.Current);
        Assert.Equal("3313", dlg.Selected!.Id);

        dlg.Selected = dlg.Filtered.Single(g => g.Name == "09С31");
        dlg.ConfirmCommand.Execute(null);
        await task;

        Assert.Equal("3031", db.Services.Settings.MyGroupId);
        Assert.Equal("09С31", shell.GroupName);
        Assert.True(changed);
    }

    [Fact]
    public async Task StartAsync_With_Data_Opens_Schedule()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        await shell.StartAsync(allowNetwork: false);

        var schedule = Assert.IsType<ScheduleViewModel>(shell.Current);
        Assert.NotEmpty(schedule.Title);
        Assert.True(shell.MainSections[0].IsActive);
    }

    [Fact]
    public async Task StartAsync_Without_Data_Shows_Error_State_And_Retry_Works()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        using var services = AppServices.Create(dir);
        var shell = new ShellViewModel(services);

        await shell.StartAsync(allowNetwork: false);

        var error = Assert.IsType<ErrorStateViewModel>(shell.Current);
        Assert.Equal("Не удалось загрузить расписание", error.Title);

        // Data appears (e.g. network is back) → retry lands on the schedule.
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-timetable.xml"));
        await services.Parser.RefreshAsync(xmlOverride: xml);
        await error.RetryCommand.ExecuteAsync(null);
        Assert.IsType<ScheduleViewModel>(shell.Current);
    }
}
