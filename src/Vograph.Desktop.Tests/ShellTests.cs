using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ShellTests : UiTest
{
    private static (TestDb Db, ShellViewModel Shell) Make()
    {
        var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        return (db, new ShellViewModel(db.Services));
    }

    // Mirrors ViewModelBaseTests.Probe: the smallest ViewModelBase that exposes RunAsync for a gated call.
    private sealed class Probe(AppServices app) : ViewModelBase(app)
    {
        public Task<string?> Run(Func<string> f) => RunAsync(f, "probe");
    }

    [AvaloniaFact]
    public void Every_Section_Resolves_To_Its_Real_ViewModel()
    {
        var (db, shell) = Make();
        using (db)
        {
            Assert.Equal(SectionKey.Schedule, shell.CurrentKey);
            Assert.IsType<Features.Week.WeekViewModel>(shell.Section<ViewModelBase>(SectionKey.Week));
            Assert.IsType<Features.Summary.SummaryViewModel>(shell.Section<ViewModelBase>(SectionKey.Summary));
            Assert.IsType<Features.Teachers.TeachersViewModel>(shell.Section<ViewModelBase>(SectionKey.Teachers));
            Assert.IsType<Features.Maps.MapsViewModel>(shell.Section<ViewModelBase>(SectionKey.Maps));
            Assert.IsType<Features.Friends.FriendsViewModel>(shell.Section<ViewModelBase>(SectionKey.Friends));
            Assert.IsType<Features.Homeworks.HomeworkViewModel>(shell.Section<ViewModelBase>(SectionKey.Homework));
            Assert.IsType<Features.Preferences.SettingsViewModel>(shell.Section<ViewModelBase>(SectionKey.Settings));

            shell.NavigateCommand.Execute("Week");
            Assert.Equal(SectionKey.Week, shell.CurrentKey);
            Assert.Same(shell.Current, shell.Section<ViewModelBase>(SectionKey.Week)); // cached instance
            Assert.True(shell.MainSections[1].IsActive);
            Assert.False(shell.MainSections[0].IsActive);
        }
    }

    [AvaloniaFact]
    public void Group_Card_Shows_My_Group()
    {
        var (db, shell) = Make();
        using (db)
        {
            Assert.Equal("А863С", shell.GroupName);
            Assert.Contains("неделя", shell.GroupSubtitle);
        }
    }

    [AvaloniaFact]
    public async Task Window_Renders_Both_Themes_Hotkeys_Navigate_And_Collapse_Persists()
    {
        var (db, shell) = Make();
        using (db)
        {
            await shell.StartAsync(allowNetwork: false); // the frames show a composed day, not an empty control
            var window = new MainWindow { DataContext = shell };
            window.Show();
            window.Focus();

            SetTheme(ThemeVariant.Dark);
            Frames.Capture(window, "shell-dark");
            SetTheme(ThemeVariant.Light);
            Frames.Capture(window, "shell-light");

            window.KeyPress(Key.D3, RawInputModifiers.Control, PhysicalKey.Digit3, null);
            Assert.Equal(SectionKey.Summary, shell.CurrentKey);

            window.KeyPress(Key.D1, RawInputModifiers.Control, PhysicalKey.Digit1, null);
            var schedule = Assert.IsType<ScheduleViewModel>(shell.Current);
            var before = schedule.DayOffset;
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.Equal(before + 1, schedule.DayOffset);
            window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            Assert.Equal(0, schedule.DayOffset);

            // With the group picker open its search box owns the arrow keys.
            var picker = shell.OpenGroupPickerCommand.ExecuteAsync(null);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (shell.Dialogs.Current is null && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
            Pump();
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.Equal(0, schedule.DayOffset);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await picker;

            window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);
            Assert.True(shell.SidebarCollapsed);
            Assert.True(shell.MainSections[0].IsCompact);
            Assert.True(UiPrefs.Load(db.Services.Prefs.FilePath).SidebarCollapsed);
            Pump(); // let the 232→64 width transition and the section cross-fade finish before the frame
            Frames.Capture(window, "shell-collapsed-light");

            AssertNoBindingErrors();
        }
    }

    [AvaloniaFact]
    public void Theme_Toggle_Flips_Actual_Variant()
    {
        var (db, shell) = Make();
        using (db)
        {
            shell.ToggleThemeCommand.Execute(null);
            var first = Application.Current!.ActualThemeVariant;
            shell.ToggleThemeCommand.Execute(null);
            Assert.NotEqual(first, Application.Current.ActualThemeVariant);
        }
    }

    [AvaloniaFact]
    public void Language_Change_Relabels_Sections()
    {
        var (db, shell) = Make();
        using (db)
        {
            db.Services.Loc.SetLanguage("en");
            Assert.Equal("Schedule", shell.MainSections[0].Label);
            db.Services.Loc.SetLanguage("ru");
        }
    }

    // R37b: an [AvaloniaFact] runs its body on the headless dispatcher thread, so this reproduces the real
    // shutdown path — App.axaml.cs runs `desktop.Exit += (_, _) => services.Dispose();` on the UI thread —
    // in a way ViewModelBaseTests' plain [Fact] cannot. Before the fix, GatedAsync's release after `await
    // run()` was posted back to this same (now UI-thread-blocked) dispatcher and never ran, so Dispose()
    // always paid the full 2 s CoreGate.Wait timeout. After the fix the gate is released on the pool thread
    // that ran the work, so Dispose() unblocks as soon as the ~200 ms probe call finishes.
    [AvaloniaFact]
    public async Task Dispose_On_The_UI_Thread_Does_Not_Deadlock_On_A_Gated_Call()
    {
        using var db = TestDb.Create();
        var vm = new Probe(db.Services);
        var inGate = new ManualResetEventSlim();

        var work = vm.Run(() => { inGate.Set(); Thread.Sleep(200); return "done"; });
        inGate.Wait(TestContext.Current.CancellationToken);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        db.Services.Dispose(); // synchronous, on the UI thread — must not deadlock into the 2 s gate wait
        sw.Stop();

        Assert.InRange(sw.ElapsedMilliseconds, 150, 1500); // checked first: a 2 s deadlock is the primary symptom
        Assert.Equal("done", await work);
    }
}
