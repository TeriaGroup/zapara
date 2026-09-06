using Vograph.Core.Models;
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

    [Fact]
    public async Task StartAsync_With_Data_Never_Shows_The_Loading_State()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var seen = new List<Type>();
        shell.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ShellViewModel.Current) && shell.Current is { } c) seen.Add(c.GetType()); };

        await shell.StartAsync(allowNetwork: false);

        Assert.IsType<ScheduleViewModel>(shell.Current);
        Assert.DoesNotContain(typeof(LoadingViewModel), seen); // cache-first: the loading state is for an empty database only
    }

    [Fact]
    public async Task Refresh_Writes_New_Timetable_Under_The_Gate_And_Notifies()
    {
        using var db = TestDb.Create();
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-timetable.xml"))
            .Replace("<Discipline>лек ИСТОРИЯ</Discipline>", "<Discipline>лек ФИЛОСОФИЯ</Discipline>");
        var handler = new FakeHttpHandler { Respond = _ => FakeHttpHandler.Bytes(System.Text.Encoding.UTF8.GetBytes(xml)) };
        db.Services.Refresher = new ScheduleRefresher(handler);
        var shell = new ShellViewModel(db.Services);
        var changed = 0;
        shell.ScheduleChanged += () => changed++;

        var ok = await shell.RefreshScheduleAsync(force: true, quiet: false);

        Assert.True(ok);
        Assert.Equal(1, changed);
        Assert.Contains(db.Services.Db.GetAllLessonsForGroup("3313"), l => l.SubjectRaw == "лек ФИЛОСОФИЯ");
        Assert.Single(db.Services.Toasts.Items, t => t.Text == "Расписание обновлено");
        Assert.Equal(1, db.Services.CoreGate.CurrentCount);
    }

    [Fact]
    public async Task Refresh_Failure_Toasts_Once_When_Quiet()
    {
        using var db = TestDb.Create();
        var handler = new FakeHttpHandler { Respond = _ => throw new HttpRequestException("offline") };
        db.Services.Refresher = new ScheduleRefresher(handler);
        var shell = new ShellViewModel(db.Services);

        Assert.False(await shell.RefreshScheduleAsync(force: false, quiet: true));
        Assert.False(await shell.RefreshScheduleAsync(force: false, quiet: true));

        Assert.Single(db.Services.Toasts.Items, t => t.Text.StartsWith("Не удалось обновить расписание"));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, "2026-09-05T10:00:00.0000000Z", true)]      // fetch 26 h ago, never checked
    [InlineData("2026-09-06T00:00:00.0000000Z", null, false)]     // checked 12 h ago
    [InlineData("2026-09-05T11:00:00.0000000Z", null, true)]      // checked 25 h ago
    public void ShouldAutoCheck_Follows_The_24h_Rule(string? lastCheck, string? lastFetch, bool expected)
    {
        var s = new Settings { LastAutoCheckAt = lastCheck, LastFetchedAt = lastFetch };
        Assert.Equal(expected, ShellViewModel.ShouldAutoCheck(s, new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task Register_Detaches_The_Previous_Section()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        await shell.StartAsync(allowNetwork: false);
        var first = Assert.IsType<ScheduleViewModel>(shell.Current);
        var reloads = 0;
        // Every Apply starts with Lessons.Clear(), which raises Reset even on an empty collection, so a
        // recompose shows up here whatever it produces. Title would not: recomposing the same group at the
        // same offset yields the identical string, and the [ObservableProperty] setter drops equal values.
        first.Lessons.CollectionChanged += (_, _) => reloads++;

        // Positive control: while first is still the registered section the shell event does reach it.
        shell.RaiseScheduleChanged();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (reloads == 0 && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(reloads > 0, "an attached section must recompose, otherwise the assertion below proves nothing");
        await Task.Delay(150, TestContext.Current.CancellationToken); // let that recompose finish before the counter is reused

        shell.Register(SectionKey.Schedule, () => new ScheduleViewModel(db.Services, shell));
        reloads = 0;
        shell.RaiseGroupChanged();
        shell.RaiseScheduleChanged();
        await Task.Delay(150, TestContext.Current.CancellationToken);

        Assert.Equal(0, reloads); // the detached section ignores shell events
    }
}
