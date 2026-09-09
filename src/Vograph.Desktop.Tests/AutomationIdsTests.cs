using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>The ids UiVerify drives must exist in the rendered window; this is the headless half of that contract.</summary>
public class AutomationIdsTests : UiTest
{
    private static readonly DateTime Mon7 = new(2026, 9, 7, 8, 0, 0);

    private static HashSet<string> Ids(Window window) =>
        window.GetVisualDescendants().OfType<Control>().Select(AutomationProperties.GetAutomationId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet()!;

    /// <summary>What the every-id test below cannot say, because a set has no order: the segmented control numbers
    /// its buttons by position. UiVerify picks a theme by pressing «SettingsTheme.1» and expects the middle
    /// segment, so the index has to follow the visual order rather than merely exist somewhere in the window.
    /// (The id lists this test used to walk were a strict subset of the scheme below — overlap, not coverage.)</summary>
    [AvaloniaFact]
    public async Task Settings_Segments_Are_Numbered_In_Visual_Order()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services) { Clock = () => Mon7 };
        shell.Register(SectionKey.Schedule, () => new Features.Schedule.ScheduleViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Settings, () => new Features.Preferences.SettingsViewModel(db.Services, shell, () => Mon7));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Pump();

        shell.NavigateTo(SectionKey.Settings);
        await Waits.Until(() => ((Features.Preferences.SettingsViewModel)shell.Current!).GroupName == "А863С", "settings");
        Pump();

        var segment = window.GetVisualDescendants().OfType<SegmentedControl>().First(s => s.Name == "SettingsTheme");
        var buttons = segment.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(new[] { "SettingsTheme.0", "SettingsTheme.1", "SettingsTheme.2" }, buttons.Select(AutomationProperties.GetAutomationId));
        Assert.Equal(new[] { "Как в системе", "Светлая", "Тёмная" }, buttons.Select(b => b.Content as string));
        AssertNoBindingErrors();
    }

    /// <summary>Every identifier of the task brief's scheme — the whole table, not the third of it the test above
    /// happens to walk past: deleting any one of them from the markup has to fail this suite, because UiVerify
    /// addresses the real app by exactly these strings and a missing one reads there as an app regression
    /// (T12-R6). Grouped as the table groups them, so a diff against the brief is a diff against this array.</summary>
    private static readonly string[] Scheme =
    {
        "Win.Minimize", "Win.Maximize", "Win.Close",
        "Shell.SidebarToggle", "Shell.GroupCard", "Shell.ThemeToggle", "Nav.Update",
        "Nav.Schedule", "Nav.Week", "Nav.Summary", "Nav.Teachers", "Nav.Maps", "Nav.Friends", "Nav.Homework", "Nav.Settings",
        "Schedule.Title", "Schedule.Subtitle", "Schedule.Prev", "Schedule.Next", "Schedule.Today",
        "ScheduleSegment.0", "ScheduleSegment.1", "ScheduleSegment.2",
        "Lesson.Title", "Lesson.Rename", "Lesson.Homework", "Lesson.Map", "Lesson.Hw",
        "WeekSegment.0", "WeekSegment.1", "Week.Day",
        "Summary.Total", "SummarySegment.0", "SummarySegment.1", "SummarySegment.2",
        "Teachers.Search", "Teachers.OnlyMine", "Teachers.List", "Teachers.Retry",
        "Maps.ToNext", "MapsBuilding.0", "MapsBuilding.1", "Maps.Floor", "Maps.ZoomIn", "Maps.ZoomOut", "Maps.Fit",
        "Maps.Reset", "Maps.Fullscreen", "Maps.More", "Maps.Plan", "MapsFull.Close", "MapsFull.ZoomIn",
        "Friends.Add", "Friend.Color", "Friend.Names", "Friend.Enabled", "Friend.Remove", "Friends.Strictness", "Friends.AlwaysAll",
        "Homework.Add", "Homework.Done", "Homework.Edit", "Homework.Delete",
        "SettingsTheme.0", "SettingsTheme.1", "SettingsTheme.2", "Account.Card", "Account.Status", "Account.Login",
        "Settings.CompactSidebar", "Settings.Animations", "Settings.ChangeGroup", "Settings.ParityInvert", "Settings.Refresh",
        "Settings.NotifyEnabled", "Settings.NotifyTime1", "Settings.NotifyTime2", "Settings.SaveTimes", "Settings.TestNotification",
        "Settings.Export", "Settings.Import", "Settings.Qr", "Settings.LanSync", "Settings.LanAddress",
        "Updates.AutoUpdate", "Updates.Status", "Updates.Check", "Updates.Install", "Updates.Releases",
        "About.Version", "About.Releases", "About.DataFolder",
        "Dialog.Confirm", "Dialog.Cancel", "Dialog.Reset", "Dialog.Search", "Dialog.List", "Dialog.Text",
        "Dialog.Name", "Dialog.Note", "Dialog.Inc", "Dialog.Dec",
        "Toast",
    };

    /// <summary>
    /// One pass over the whole window collecting ids: eight sections, the fullscreen map overlay, the three
    /// dialogs that between them carry every Dialog.* id, and a toast. IsVisible="False" leaves a control in the
    /// visual tree, so the state-gated entries (Nav.Update, Updates.Install, Teachers.Retry, Dialog.Reset) need
    /// no contrived state; what really needs data is the item templates — lessons, week days, floor pills,
    /// friends, homework rows — which the seeded fixture provides.
    /// </summary>
    [AvaloniaFact]
    public async Task Every_Id_Of_The_Scheme_Is_Somewhere_In_The_Window()
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
        shell.Register(SectionKey.Schedule, () => new Features.Schedule.ScheduleViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Week, () => new Features.Week.WeekViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Summary, () => new Features.Summary.SummaryViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Teachers, () => new Features.Teachers.TeachersViewModel(db.Services, shell, () => Mon7, allowNetwork: false));
        shell.Register(SectionKey.Maps, () => new Features.Maps.MapsViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Friends, () => new Features.Friends.FriendsViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Homework, () => new Features.Homeworks.HomeworkViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Settings, () => new Features.Preferences.SettingsViewModel(db.Services, shell, () => Mon7));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();

        var loaded = new Dictionary<SectionKey, Func<ViewModelBase, bool>>
        {
            [SectionKey.Schedule] = vm => ((Features.Schedule.ScheduleViewModel)vm).Lessons.Count == 2,
            [SectionKey.Week] = vm => ((Features.Week.WeekViewModel)vm).Days.Count == 6,
            [SectionKey.Summary] = vm => ((Features.Summary.SummaryViewModel)vm).TotalText != "—",
            [SectionKey.Teachers] = vm => ((Features.Teachers.TeachersViewModel)vm).Items.Count > 0,
            [SectionKey.Maps] = vm => ((Features.Maps.MapsViewModel)vm).Floors.Count > 0,
            [SectionKey.Friends] = vm => ((Features.Friends.FriendsViewModel)vm).Friends.Count == 1,
            [SectionKey.Homework] = vm => ((Features.Homeworks.HomeworkViewModel)vm).Groups.Count > 0,
            [SectionKey.Settings] = vm => ((Features.Preferences.SettingsViewModel)vm).GroupName == "А863С",
        };
        var ids = new HashSet<string>();
        foreach (var key in Enum.GetValues<SectionKey>())
        {
            shell.NavigateTo(key);
            var vm = shell.Current!;
            await Waits.Until(() => loaded[key](vm), $"section {key} loaded");
            Pump();
            ids.UnionWith(Ids(window));
        }

        // The fullscreen plan is an overlay over the whole window, not a section.
        var maps = shell.Section<Features.Maps.MapsViewModel>(SectionKey.Maps);
        maps.ToggleFullscreenCommand.Execute(null);
        Pump();
        ids.UnionWith(Ids(window));
        maps.ToggleFullscreenCommand.Execute(null);
        Pump();

        // Between them these three carry every Dialog.* id. ShowAsync only returns when the dialog closes, so it
        // is held and cancelled rather than awaited in place.
        foreach (var dialog in new DialogViewModelBase[]
                 {
                     new GroupPickerDialogViewModel(db.Services.Db.GetAllGroups(), TestDb.MyGroupId),
                     new RenameDialogViewModel("Матан", TestDb.MathSubject, (int)DayOfWeek.Monday, db.Services.Overrides.GetOverride(TestDb.MathSubject, "global")),
                     new HomeworkDialogViewModel("Матан", _ => Mon7.AddDays(7)),
                 })
        {
            var showing = shell.Dialogs.ShowAsync(dialog);
            Pump();
            ids.UnionWith(Ids(window));
            dialog.Cancel();
            await showing;
            Pump();
        }

        db.Services.Toasts.Info("готово");
        Pump();
        ids.UnionWith(Ids(window));

        var missing = Scheme.Except(ids).ToArray();
        Assert.DoesNotContain(ids, id => id.StartsWith("SettingsLanguage", StringComparison.Ordinal));
        Assert.True(missing.Length == 0, $"{missing.Length} id(s) of the brief's scheme are not in the window: {string.Join(", ", missing)}");
        AssertNoBindingErrors();
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Offline_Switch_Reads_The_Environment(string? value, bool expected) =>
        Assert.Equal(expected, App.ReadOfflineSwitch(_ => value));
}
