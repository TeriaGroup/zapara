using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Vograph.Desktop.Features.Friends;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Features.Maps;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Features.Summary;
using Vograph.Desktop.Features.Teachers;
using Vograph.Desktop.Features.Week;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>One pass over all eight sections: both themes render without binding errors and the language switch relabels every title.</summary>
public class SectionsRenderTests : UiTest
{
    private static readonly DateTime Mon7 = new(2026, 9, 7, 8, 0, 0);

    private static async Task WaitAsync(Func<bool> done)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(done(), "section did not finish loading in time");
    }

    [AvaloniaFact]
    public async Task All_Sections_Render_In_Both_Themes_And_Relabel_On_Language_Change()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        // Every door out of the process is a fake: no maps download, no GitHub call, no OS dialog, no browser.
        db.Services.MapFiles = new FakeMapFiles(Path.Combine(db.Dir, "maps"), ("ГК", 4));
        db.Services.Launcher = new FakeLauncher();
        db.Services.FileDialogs = new FakeFileDialogs();
        db.Services.UpdateSource = new FakeUpdateSource();
        await db.Services.Lecturers.LoadXmlAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-lecturers.xml")));
        var shell = new ShellViewModel(db.Services) { Clock = () => Mon7 };
        shell.Register(SectionKey.Schedule, () => new ScheduleViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Week, () => new WeekViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Summary, () => new SummaryViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Teachers, () => new TeachersViewModel(db.Services, shell, () => Mon7, allowNetwork: false));
        shell.Register(SectionKey.Maps, () => new MapsViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Friends, () => new FriendsViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Homework, () => new HomeworkViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Settings, () => new SettingsViewModel(db.Services, shell, () => Mon7));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();

        var loaded = new Dictionary<SectionKey, Func<ViewModelBase, bool>>
        {
            [SectionKey.Schedule] = vm => ((ScheduleViewModel)vm).Lessons.Count == 2,
            [SectionKey.Week] = vm => ((WeekViewModel)vm).Days.Count == 6,
            [SectionKey.Summary] = vm => ((SummaryViewModel)vm).TotalText != "—",
            [SectionKey.Teachers] = vm => ((TeachersViewModel)vm).Items.Count > 0,
            [SectionKey.Maps] = vm => ((MapsViewModel)vm).Image is not null,
            [SectionKey.Friends] = vm => ((FriendsViewModel)vm).Friends.Count == 1,
            [SectionKey.Homework] = vm => ((HomeworkViewModel)vm).Groups.Count > 0,
            [SectionKey.Settings] = vm => ((SettingsViewModel)vm).GroupName == "А863С",
        };
        foreach (var key in Enum.GetValues<SectionKey>())
        {
            shell.NavigateTo(key);
            var vm = shell.Current!;
            await WaitAsync(() => loaded[key](vm));
            Pump();
            SetTheme(ThemeVariant.Dark);
            Frames.Capture(window, $"section-{key.ToString().ToLowerInvariant()}-dark");
            SetTheme(ThemeVariant.Light);
            Frames.Capture(window, $"section-{key.ToString().ToLowerInvariant()}-light");
        }
        AssertNoBindingErrors();

        db.Services.Loc.SetLanguage("en");
        Pump();
        Assert.Equal(new[] { "Schedule", "Week", "Summary" }, shell.MainSections.Select(s => s.Label));
        Assert.Equal("Settings", shell.SettingsSection.Label);
        Assert.Equal("Week", ((WeekViewModel)shell.Section<ViewModelBase>(SectionKey.Week)).Title);
        Assert.Equal("Teachers", ((TeachersViewModel)shell.Section<ViewModelBase>(SectionKey.Teachers)).Title);
        Assert.Equal("Homework", ((HomeworkViewModel)shell.Section<ViewModelBase>(SectionKey.Homework)).Title);
        Assert.Equal("Settings", ((SettingsViewModel)shell.Section<ViewModelBase>(SectionKey.Settings)).Title);
        db.Services.Loc.SetLanguage("ru");
        AssertNoBindingErrors();
    }
}
