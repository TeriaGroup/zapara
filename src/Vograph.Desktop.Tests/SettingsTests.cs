using System.Globalization;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class SettingsTests : UiTest
{
    private static readonly DateTime Sun6 = new(2026, 9, 6, 15, 0, 0);

    [Fact]
    public void Version_Tag_Comes_From_The_Assembly()
    {
        Assert.Equal("windows-v2.0.0", AppVersion.Tag);
        Assert.Equal("2.0.0", AppVersion.Short);
        Assert.True(Vograph.Core.Services.AutoUpdateService.IsNewer("windows-v2.1.0", AppVersion.Tag));
        Assert.False(Vograph.Core.Services.AutoUpdateService.IsNewer("windows-v1.2.2", AppVersion.Tag)); // never "update" to the WPF release
    }

    [Fact]
    public async Task Appearance_Settings_Persist()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new SettingsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        Assert.Equal(0, vm.ThemeIndex);
        Assert.Equal(0, vm.LanguageIndex);

        vm.ThemeIndex = 2; // no ThemeService in a plain unit test: the preference is still written
        Assert.Equal(ThemeChoice.Dark, db.Services.Prefs.Theme);
        Assert.Equal(ThemeChoice.Dark, UiPrefs.Load(db.Services.Prefs.FilePath).Theme);

        vm.Animations = false;
        Assert.False(UiPrefs.Load(db.Services.Prefs.FilePath).Animations);

        vm.CompactSidebar = true;
        Assert.True(shell.SidebarCollapsed);
        shell.SidebarCollapsed = false; // Ctrl+B elsewhere is reflected back
        Assert.False(vm.CompactSidebar);

        vm.LanguageIndex = 1;
        Assert.Equal("en", db.Services.Loc.Language);
        Assert.Equal("Schedule", shell.MainSections[0].Label);
        await Waits.Until(() => db.Services.Db.GetSettings().Language == "en", "language setting saved");
        db.Services.Loc.SetLanguage("ru");
    }

    [Fact]
    public async Task Schedule_Card_Inverts_Parity_And_Shows_Dates()
    {
        using var db = TestDb.Create();
        var s = db.Services.Db.GetSettings();
        s.LastFetchedAt = "2026-09-06T12:00:00.0000000Z";
        s.LastAutoCheckAt = null;
        db.Services.Db.SaveSettings(s);
        var shell = new ShellViewModel(db.Services);
        var changed = 0;
        shell.ScheduleChanged += () => changed++;
        var vm = new SettingsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();

        Assert.Equal("А863С", vm.GroupName);
        var expected = DateTime.Parse("2026-09-06T12:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime().ToString("dd.MM", CultureInfo.InvariantCulture);
        Assert.StartsWith($"обновлено {expected}", vm.UpdatedText); // local time: the day can roll over east of UTC+11
        Assert.Equal("автопроверка ещё не было", vm.AutoCheckText);
        Assert.False(vm.ParityInvert);

        vm.ParityInvert = true;
        await Waits.Until(() => db.Services.Db.GetSettings().ParityInvert, "parity invert saved");
        await Waits.Until(() => changed == 1, "schedule changed raised");
    }

    /// <summary>«Обновить расписание» forces a full download (no HEAD), so the fake answers the GET with the fixture XML.</summary>
    [Fact]
    public async Task Refresh_Delegates_To_The_Shell()
    {
        using var db = TestDb.Create();
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-timetable.xml"));
        var handler = new FakeHttpHandler { Respond = _ => FakeHttpHandler.Bytes(System.Text.Encoding.UTF8.GetBytes(xml)) };
        db.Services.Refresher = new ScheduleRefresher(handler);
        var shell = new ShellViewModel(db.Services);
        var vm = new SettingsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method); // force: straight to the download
        Assert.Contains(db.Services.Toasts.Items, t => t.Text == "Расписание обновлено");
        Assert.False(vm.IsRefreshing);
        Assert.False(shell.IsRefreshing);
        Assert.NotEqual("автопроверка ещё не было", vm.AutoCheckText); // LoadAsync after the refresh shows the new stamp
    }

    [Fact]
    public async Task About_Links_Go_Through_The_Launcher()
    {
        using var db = TestDb.Create();
        var launcher = new FakeLauncher();
        db.Services.Launcher = launcher;
        var shell = new ShellViewModel(db.Services);
        var vm = new SettingsViewModel(db.Services, shell, () => Sun6);

        Assert.Equal("Версия windows-v2.0.0", vm.VersionText);
        await vm.OpenReleasesCommand.ExecuteAsync(null);
        await vm.OpenTimetableSourceCommand.ExecuteAsync(null);
        await vm.OpenMapsSourceCommand.ExecuteAsync(null);
        await vm.OpenDataFolderCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "https://github.com/0NiLle0/zapara/releases", "https://voenmeh.ru/obrazovanie/timetables/", "https://voenmeh.ru/openmap/" }, launcher.Urls);
        Assert.Equal(db.Services.DataDir, Assert.Single(launcher.Folders));
    }

    [AvaloniaFact]
    public async Task Sidebar_Theme_Toggle_Mirrors_Into_Settings_Until_Detached()
    {
        using var db = TestDb.Create();
        var theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        db.Services.Theme = theme;
        var shell = new ShellViewModel(db.Services);
        var vm = new SettingsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        var before = vm.ThemeIndex;

        shell.ToggleThemeCommand.Execute(null); // the sidebar's quick-toggle button, not the Settings segment
        Assert.NotEqual(before, vm.ThemeIndex);
        Assert.Equal(theme.Choice == ThemeChoice.Dark ? 2 : 1, vm.ThemeIndex);

        vm.Detach();
        var afterDetach = vm.ThemeIndex;
        shell.ToggleThemeCommand.Execute(null); // flips back; Settings no longer listens once detached
        Assert.Equal(afterDetach, vm.ThemeIndex);
    }

    [AvaloniaFact]
    public async Task Detach_Disposes_The_Qr_Bitmap()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new SettingsViewModel(db.Services, shell, () => Sun6);
        await vm.ToggleQrCommand.ExecuteAsync(null);
        var qr = vm.QrImage!;

        vm.Detach();

        Assert.Null(vm.QrImage);
        Assert.False(vm.QrVisible);
        Assert.Throws<ObjectDisposedException>(() => _ = qr.PixelSize);
    }

    [AvaloniaFact]
    public async Task Settings_Render_Both_Themes_And_Theme_Segment_Switches()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        shell.Register(SectionKey.Settings, () => new SettingsViewModel(db.Services, shell, () => Sun6));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.NavigateTo(SectionKey.Settings);
        var vm = Assert.IsType<SettingsViewModel>(shell.Current);
        await Waits.Until(() => vm.GroupName == "А863С", "settings group name");
        Pump();
        SetTheme(ThemeVariant.Dark);
        Frames.Capture(window, "settings-dark");
        SetTheme(ThemeVariant.Light);
        Frames.Capture(window, "settings-light");

        var themeSeg = window.GetVisualDescendants().OfType<SegmentedControl>().First();
        Click(window, themeSeg.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Last()); // «Тёмная»
        Assert.Equal(2, vm.ThemeIndex);
        Assert.Equal(ThemeVariant.Dark, Application.Current!.ActualThemeVariant);
        Assert.Equal(ThemeChoice.Dark, db.Services.Prefs.Theme);
        AssertNoBindingErrors();
    }
}
