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
        Assert.False(await vm.CheckAsync());
        Assert.Equal(UpdateState.UpToDate, vm.State);
        Assert.Contains("windows-v2.0.0", vm.StatusText);
        Assert.Contains("15:00", vm.CheckedAt);
        Assert.True(vm.CheckedThisSession);

        source.Latest = new AutoUpdateService.UpdateInfo("windows-v1.2.2", "u", "z", "2026-08-01T00:00:00Z"); // the old WPF release
        Assert.False(await vm.CheckAsync());
        Assert.Equal(UpdateState.UpToDate, vm.State);

        source.Latest = Newer;
        Assert.True(await vm.CheckAsync());
        Assert.Equal(UpdateState.Available, vm.State);
        Assert.Equal("windows-v2.1.0", vm.LatestTag);
        Assert.Equal("05.09.2026", vm.PublishedText);
        Assert.True(vm.IsAvailable);
        Assert.Equal("1", vm.BadgeText);
        Assert.Equal("Доступна windows-v2.1.0", vm.StatusText);

        source.Latest = null;
        Assert.False(await vm.CheckAsync());
        Assert.Equal(UpdateState.Failed, vm.State);
        Assert.Equal("Релизов для Windows не найдено", vm.StatusText);

        source.Failure = new HttpRequestException("403");
        Assert.False(await vm.CheckAsync());
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
        await vm.CheckAsync();

        await vm.InstallCommand.ExecuteAsync(null); // Available -> download -> Ready -> install
        Assert.Single(source.Downloads);
        Assert.Single(installed, p => p.EndsWith("ZAPARA_windows-v2.1.0_win-x64.zip") && File.Exists(p));
        Assert.Equal(UpdateState.Ready, vm.State);
        await Waits.Until(() => vm.Progress == 1.0); // Progress<T>.Report posts its callback asynchronously
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
        await Waits.Until(() => !db.Services.Db.GetSettings().AutoUpdate, "auto-update setting saved");
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
        await Waits.Until(() => source.Checks == 1 && shell.Updates.IsAvailable);

        await vm.ActivateAsync(); // second entry in the same session: no second call
        Assert.Equal(1, source.Checks);

        // The switch off: a fresh session (new database, new shell) never calls out.
        using var quiet = TestDb.Create();
        var qs = quiet.Services.Db.GetSettings();
        qs.AutoUpdate = false;
        quiet.Services.Db.SaveSettings(qs);
        var quietSource = new FakeUpdateSource { Latest = Newer };
        quiet.Services.UpdateSource = quietSource;
        quiet.Services.AllowNetwork = true;
        var quietShell = new ShellViewModel(quiet.Services) { Clock = () => Sun6 };
        await new SettingsViewModel(quiet.Services, quietShell, () => Sun6).ActivateAsync();
        Assert.Equal(0, quietSource.Checks);
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

        await shell.Updates.CheckAsync();
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

    [Fact]
    public async Task Corrupt_Zip_Is_Rejected_Before_The_Installer()
    {
        using var db = TestDb.Create();
        var (vm, source, installed) = Make(db);
        source.Latest = Newer;
        source.Corrupt = true;
        await vm.CheckAsync();

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Empty(installed);
        Assert.Equal(UpdateState.Failed, vm.State);
        Assert.Equal("Скачанный архив повреждён — попробуйте ещё раз", vm.StatusText);
        Assert.Empty(Directory.GetFiles(vm.UpdatesDir, "*.zip")); // the bad file is not kept for a «Ready» on the next start
    }

    [Fact]
    public async Task Download_And_Apply_Failures_Have_Their_Own_Wording()
    {
        using var db = TestDb.Create();
        var (vm, source, _) = Make(db);
        source.Latest = Newer;
        source.DownloadFailure = new HttpRequestException("offline");
        await vm.CheckAsync();
        Assert.False(await vm.DownloadAsync());
        Assert.Equal("Не удалось скачать обновление: offline", vm.StatusText);

        source.DownloadFailure = null;
        await vm.CheckAsync();
        vm.Installer = _ => throw new UnauthorizedAccessException("Program Files");
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.Equal(UpdateState.Failed, vm.State);
        Assert.Equal("Не удалось запустить установку: Program Files", vm.StatusText);
    }

    [Fact]
    public async Task A_Later_Check_Clears_The_Previous_Release()
    {
        using var db = TestDb.Create();
        var (vm, source, _) = Make(db);
        source.Latest = Newer;
        Assert.True(await vm.CheckAsync());
        Assert.Equal("windows-v2.1.0", vm.LatestTag);

        source.Latest = new AutoUpdateService.UpdateInfo("windows-v2.0.0", "u", "z", "2026-09-01T00:00:00Z");
        Assert.False(await vm.CheckAsync());
        Assert.Equal(UpdateState.UpToDate, vm.State);
        Assert.Null(vm.LatestTag);
        Assert.Null(vm.PublishedText);
        Assert.False(vm.CanInstall);
        Assert.False(await vm.DownloadAsync()); // nothing to download any more
    }

    [Fact]
    public async Task Cleanup_Removes_Installed_And_Older_Downloads()
    {
        using var db = TestDb.Create();
        var (vm, _, _) = Make(db);
        Directory.CreateDirectory(vm.UpdatesDir);
        File.WriteAllBytes(Path.Combine(vm.UpdatesDir, "ZAPARA_windows-v2.0.0_win-x64.zip"), new byte[10]);      // this very version: installed
        File.WriteAllText(Path.Combine(vm.UpdatesDir, "ZAPARA_windows-v2.0.0_win-x64.zip.attempted"), "x");
        File.WriteAllBytes(Path.Combine(vm.UpdatesDir, "ZAPARA_windows-v1.2.2_win-x64.zip"), new byte[10]);      // older
        File.WriteAllBytes(Path.Combine(vm.UpdatesDir, "ZAPARA_windows-v2.0.0_win-x64.zip.part"), new byte[10]); // a torn download
        File.WriteAllBytes(Path.Combine(vm.UpdatesDir, "ZAPARA_windows-v2.1.0_win-x64.zip"), new byte[10]);      // newer: keep

        await vm.CleanupAsync();

        Assert.Equal(new[] { "ZAPARA_windows-v2.1.0_win-x64.zip" }, Directory.GetFiles(vm.UpdatesDir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Concurrent_Install_Calls_Run_The_Installer_Once()
    {
        using var db = TestDb.Create();
        var (vm, source, installed) = Make(db);
        source.Latest = Newer;
        await vm.CheckAsync();

        await Task.WhenAll(vm.InstallAsync(), vm.InstallAsync()); // direct calls bypass the command's own guard

        Assert.Single(installed);
    }

    [Theory]
    [InlineData("windows-v2.1.0", "windows-v2.1.0")]
    [InlineData("../../evil", "____evil")]   // '/' → '_', then every ".." → '_'
    [InlineData("win:dows/v2", "win_dows_v2")]
    [InlineData("windows-v2%TEMP%", "windows-v2_TEMP_")]   // cmd.exe would expand this inside the installer batch
    [InlineData("windows-v2'; rm x; '", "windows-v2___rm_x___")]
    public void Tags_Are_Sanitised_Before_Becoming_File_Names(string tag, string expected) => Assert.Equal(expected, UpdateCheckViewModel.SafeTag(tag));

    /// <summary>The check answers one question — «will the batch's flat unpack put Vograph.exe where the batch
    /// then starts it?» UpdateRunner.BuildBatch expands the archive over the install directory and runs
    /// {dir}\Vograph.exe, so an entry one folder down is not «found», it is a mis-built release that would unpack
    /// beside the app and relaunch the OLD exe — with the «attempted» marker already written, blocking retries
    /// while the card still says «Доступна». The shipped release zip is flat (verified against the built
    /// ZAPARA_win-x64.zip: Vograph.exe sits at the root).</summary>
    [Fact]
    public void LooksLikeZip_Requires_The_Exe_At_The_Archive_Root()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var good = Path.Combine(dir, "good.zip");
            File.WriteAllBytes(good, FakeUpdateSource.ReleaseZip());
            Assert.True(UpdateCheckViewModel.LooksLikeZip(good));
            var nested = Path.Combine(dir, "nested.zip");
            File.WriteAllBytes(nested, FakeUpdateSource.ReleaseZip(nested: true)); // ZAPARA_win-x64/Vograph.exe
            Assert.False(UpdateCheckViewModel.LooksLikeZip(nested));
            var garbage = Path.Combine(dir, "garbage.zip");
            File.WriteAllBytes(garbage, new byte[4096]);
            Assert.False(UpdateCheckViewModel.LooksLikeZip(garbage));
            Assert.False(UpdateCheckViewModel.LooksLikeZip(Path.Combine(dir, "missing.zip")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>The same rule end to end: a nested release is refused exactly where a corrupt one is — before the
    /// installer runs, before a shutdown is triggered, and without leaving the archive behind for a «Ready» on
    /// the next start.</summary>
    [Fact]
    public async Task Nested_Zip_Is_Rejected_Before_The_Installer()
    {
        using var db = TestDb.Create();
        var (vm, source, installed) = Make(db);
        source.Latest = Newer;
        source.Nested = true;
        await vm.CheckAsync();

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Empty(installed);
        Assert.Equal(UpdateState.Failed, vm.State);
        Assert.Equal("Скачанный архив повреждён — попробуйте ещё раз", vm.StatusText);
        Assert.Empty(Directory.GetFiles(vm.UpdatesDir, "*.zip"));
    }

    /// <summary>
    /// T12-R6's gate on «Проверить» in Settings. The switch promises «no update check» (App.axaml.cs), and until
    /// that gate landed the button reached GitHub on an offline run — AllowNetwork only covered the silent
    /// startup flow. The fake source counts what a check that got past the gate would have done: one call, a
    /// «Доступна 2.1.0» card and a sidebar badge instead of the offline reason.
    /// </summary>
    [Fact]
    public async Task Manual_Check_Reaches_No_Source_When_The_Run_Is_Offline()
    {
        using var db = TestDb.Create(); // AllowNetwork = false, as App assigns it from VOGRAPH_OFFLINE
        var (vm, source, _) = Make(db);
        source.Latest = Newer;
        Assert.False(db.Services.AllowNetwork);

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(0, source.Checks);
        Assert.Equal(UpdateState.Failed, vm.State);
        Assert.Equal("Не удалось проверить обновление: сеть отключена для этого запуска (VOGRAPH_OFFLINE)", vm.StatusText);
        Assert.Equal(SettingsViewModel.ReleasesUrl, vm.HtmlUrl);
        Assert.False(vm.IsAvailable);
        Assert.Contains("update check: skipped, network disabled for this run", File.ReadAllText(db.Services.Log.CurrentFile));
    }

    [Fact]
    public void GitHub_Source_Is_The_Default_And_Shares_The_App_Service()
    {
        using var db = TestDb.Create();
        using var extra = AppServices.Create(Path.Combine(db.Dir, "x")); // a plain instance: TestDb swaps the source for a fake
        Assert.IsType<GitHubUpdateSource>(extra.UpdateSource);
        Assert.Same(extra.AutoUpdate, Assert.IsType<GitHubUpdateSource>(extra.UpdateSource).Service);
    }
}
