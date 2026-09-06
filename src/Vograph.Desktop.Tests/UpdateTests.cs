using System.Net;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Core.Services;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class UpdateTests : UiTest
{
    private static readonly DateTime Sun6 = new(2026, 9, 6, 15, 0, 0);
    private static readonly Loc Ru = new(new I18nService("ru"));
    private static AutoUpdateService.UpdateInfo Newer => new("windows-v2.1.0", "https://github.com/0NiLle0/zapara/releases/tag/windows-v2.1.0", "https://example.test/ZAPARA_windows-v2.1.0_win-x64.zip", "2026-09-05T10:00:00Z");

    private static (UpdateCheckViewModel Vm, FakeUpdateSource Source, List<string> Installed) Make(TestDb db)
    {
        var source = new FakeUpdateSource();
        db.Services.UpdateSource = source;
        var installed = new List<string>();
        var vm = new UpdateCheckViewModel(db.Services, () => Sun6, Path.Combine(db.Dir, "updates"))
        {
            Installer = installed.Add,
            Delay = _ => Task.CompletedTask
        };
        return (vm, source, installed);
    }

    private static bool IsUpdateItem(NavItem n) => n.IsEffectivelyVisible && n.Content is string c && c == "Обновление";

    private static async Task WaitAsync(Func<bool> done)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(done(), "condition not met in time");
    }

    [Fact]
    public void Batch_Waits_For_Exit_Unpacks_And_Restarts()
    {
        var bat = UpdateRunner.BuildBatch(@"C:\Apps\Vograph", @"C:\Users\x\AppData\Local\Vograph\updates\ZAPARA_windows-v2.1.0_win-x64.zip");
        Assert.StartsWith("@echo off", bat);
        Assert.Contains("chcp 65001", bat);
        Assert.Contains("tasklist /FI \"IMAGENAME eq Vograph.exe\"", bat);
        Assert.Contains("Expand-Archive -LiteralPath 'C:\\Users\\x\\AppData\\Local\\Vograph\\updates\\ZAPARA_windows-v2.1.0_win-x64.zip'", bat);
        Assert.Contains("start \"\" \"C:\\Apps\\Vograph\\Vograph.exe\"", bat);
        Assert.Contains("del \"%~f0\"", bat);
    }

    [Fact]
    public void Friendly_Messages_For_Rate_Limits()
    {
        Assert.StartsWith("GitHub ограничил запросы", UpdateCheckViewModel.Friendly(new HttpRequestException("Response status code does not indicate success: 403 (rate limit exceeded).", null, HttpStatusCode.Forbidden), Ru));
        Assert.StartsWith("GitHub ограничил запросы", UpdateCheckViewModel.Friendly(new HttpRequestException("429 Too Many Requests"), Ru));
        Assert.Equal("Не удалось проверить обновление: offline", UpdateCheckViewModel.Friendly(new HttpRequestException("offline"), Ru));
    }

    [Fact]
    public async Task Check_Reports_Every_State()
    {
        using var db = TestDb.Create();
        var (vm, source, _) = Make(db);
        Assert.Equal(UpdateState.Idle, vm.State);

        source.Latest = new AutoUpdateService.UpdateInfo("windows-v2.0.0", "u", "z", "2026-09-01T00:00:00Z");
        Assert.False(await vm.CheckAsync(manual: true));
        Assert.Equal(UpdateState.UpToDate, vm.State);
        Assert.Contains("windows-v2.0.0", vm.StatusText);
        Assert.Contains("15:00", vm.CheckedAt);
        Assert.True(vm.CheckedThisSession);

        source.Latest = new AutoUpdateService.UpdateInfo("windows-v1.2.2", "u", "z", "2026-08-01T00:00:00Z"); // the old WPF release
        Assert.False(await vm.CheckAsync(manual: true));
        Assert.Equal(UpdateState.UpToDate, vm.State);

        source.Latest = Newer;
        Assert.True(await vm.CheckAsync(manual: true));
        Assert.Equal(UpdateState.Available, vm.State);
        Assert.Equal("windows-v2.1.0", vm.LatestTag);
        Assert.Equal("05.09.2026", vm.PublishedText);
        Assert.True(vm.IsAvailable);
        Assert.Equal("1", vm.BadgeText);
        Assert.Equal("Доступна windows-v2.1.0", vm.StatusText);

        source.Latest = null;
        Assert.False(await vm.CheckAsync(manual: true));
        Assert.Equal(UpdateState.Failed, vm.State);
        Assert.Equal("Релизов для Windows не найдено", vm.StatusText);

        source.Failure = new HttpRequestException("403");
        Assert.False(await vm.CheckAsync(manual: true));
        Assert.Equal(UpdateState.Failed, vm.State);
        Assert.StartsWith("GitHub ограничил запросы", vm.StatusText);
        Assert.False(vm.IsAvailable);
    }

    [Fact]
    public async Task Download_Then_Install_Runs_The_Installer_Once()
    {
        using var db = TestDb.Create();
        var (vm, source, installed) = Make(db);
        source.Latest = Newer;
        await vm.CheckAsync(manual: true);

        await vm.InstallCommand.ExecuteAsync(null); // Available -> download -> Ready -> install
        Assert.Single(source.Downloads);
        Assert.Single(installed, p => p.EndsWith("ZAPARA_windows-v2.1.0_win-x64.zip") && File.Exists(p));
        Assert.Equal(UpdateState.Ready, vm.State);
        await WaitAsync(() => vm.Progress == 1.0); // Progress<T>.Report posts its callback asynchronously
        Assert.Equal(1.0, vm.Progress);
    }

    [Fact]
    public async Task Startup_Flow_Is_Silent_And_Honours_The_Switch()
    {
        using var db = TestDb.Create();
        var (vm, source, installed) = Make(db);
        source.Latest = Newer;

        var s = db.Services.Db.GetSettings();
        s.AutoUpdate = false;
        db.Services.Db.SaveSettings(s);
        await vm.RunStartupFlowAsync();
        Assert.Equal(0, source.Checks);
        Assert.Empty(installed);

        s.AutoUpdate = true;
        db.Services.Db.SaveSettings(s);
        await vm.RunStartupFlowAsync();
        Assert.Equal(1, source.Checks);
        Assert.Single(installed);
        Assert.Contains(db.Services.Toasts.Items, t => t.Text == "Обновляюсь до windows-v2.1.0…");
        Assert.DoesNotContain(db.Services.Toasts.Items, t => t.Kind == ToastKind.Bad);
    }

    /// <summary>R45: a locked install directory (Expand-Archive cannot write under Program Files, an AV lock, disk
    /// full) must not toast-and-shutdown on every relaunch forever. The marker written before the first attempt
    /// gates the silent path on the next one — modelled here as two VM instances (a fresh relaunch never carries
    /// the previous one's in-memory state) that share the same updatesDir (disk state does survive a relaunch).</summary>
    [Fact]
    public async Task Startup_Flow_Does_Not_Repeat_For_An_Already_Attempted_Tag()
    {
        using var db = TestDb.Create();
        var updatesDir = Path.Combine(db.Dir, "updates");
        var source = new FakeUpdateSource { Latest = Newer };
        db.Services.UpdateSource = source;
        var installed = new List<string>();

        var vm1 = new UpdateCheckViewModel(db.Services, () => Sun6, updatesDir)
        {
            Installer = installed.Add,
            Shutdown = () => { },
            Delay = _ => Task.CompletedTask
        };
        await vm1.RunStartupFlowAsync();
        Assert.Single(installed);
        Assert.Single(db.Services.Toasts.Items, t => t.Text == "Обновляюсь до windows-v2.1.0…");

        var vm2 = new UpdateCheckViewModel(db.Services, () => Sun6, updatesDir) // the app relaunched: a fresh VM
        {
            Installer = installed.Add,
            Shutdown = () => { },
            Delay = _ => Task.CompletedTask
        };
        await vm2.RunStartupFlowAsync();
        Assert.Single(installed); // the installer is not called a second time
        Assert.Single(db.Services.Toasts.Items, t => t.Text == "Обновляюсь до windows-v2.1.0…"); // no second toast
        Assert.True(vm2.IsAvailable); // still the visible route: sidebar item, badge, Settings card
    }

    [Fact]
    public async Task AutoUpdate_Switch_Persists()
    {
        using var db = TestDb.Create();
        var (vm, _, _) = Make(db);
        await vm.LoadAsync();
        Assert.True(vm.AutoUpdate);
        vm.AutoUpdate = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (db.Services.Db.GetSettings().AutoUpdate && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.False(db.Services.Db.GetSettings().AutoUpdate);
    }

    /// <summary>Spec 5.8: entering Settings checks once per session, and only while the switch is on.</summary>
    [Fact]
    public async Task Settings_Checks_Once_Per_Session_While_AutoUpdate_Is_On()
    {
        using var db = TestDb.Create();
        var source = new FakeUpdateSource { Latest = Newer };
        db.Services.UpdateSource = source;
        db.Services.AllowNetwork = true; // the fake stands in for GitHub: no socket is opened
        var shell = new ShellViewModel(db.Services) { Clock = () => Sun6 };
        var vm = new SettingsViewModel(db.Services, shell, () => Sun6);

        await vm.ActivateAsync();
        await WaitAsync(() => source.Checks == 1 && shell.Updates.IsAvailable);

        await vm.ActivateAsync(); // second entry in the same session: no second call
        Assert.Equal(1, source.Checks);

        shell.Updates.AutoUpdate = false;
        await new SettingsViewModel(db.Services, shell, () => Sun6).ActivateAsync();
        Assert.Equal(1, source.Checks);
    }

    [Fact]
    public async Task Update_Dialog_Confirms_Into_Install()
    {
        using var db = TestDb.Create(seedPersonalization: false); // the dialog ctor reads Loc.Current
        Assert.NotNull(db);
        var dlg = new UpdateDialogViewModel("windows-v2.1.0", "05.09.2026");
        Assert.Equal("Доступна windows-v2.1.0", dlg.Title);
        Assert.Equal("05.09.2026", dlg.PublishedText);
        dlg.ConfirmCommand.Execute(null);
        Assert.True(await dlg.Completion);
    }

    [AvaloniaFact]
    public async Task Sidebar_Shows_The_Update_Item_And_Settings_Card_Renders()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var source = new FakeUpdateSource { Latest = Newer };
        db.Services.UpdateSource = source;
        var shell = new ShellViewModel(db.Services) { Clock = () => Sun6 };
        shell.Updates.Installer = _ => { };
        await shell.StartAsync(allowNetwork: false); // no silent flow without network
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Pump();
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<NavItem>(), IsUpdateItem);

        await shell.Updates.CheckAsync(manual: true);
        Pump();
        var item = Assert.Single(window.GetVisualDescendants().OfType<NavItem>(), IsUpdateItem);
        Assert.Equal("1", item.Badge);

        shell.NavigateTo(SectionKey.Settings);
        Pump();
        window.MouseWheel(new Point(640, 500), new Vector(0, -9)); // the updates card sits below the fold
        Pump();
        SetTheme(ThemeVariant.Dark);
        Frames.Capture(window, "settings-update-dark");

        Click(window, item);
        var dlg = Assert.IsType<UpdateDialogViewModel>(shell.Dialogs.Current);
        Frames.Capture(window, "update-dialog-dark");
        dlg.CancelCommand.Execute(null);
        AssertNoBindingErrors();
    }
}
