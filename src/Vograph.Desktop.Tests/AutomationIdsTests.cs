using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>The ids UiVerify drives must exist in the rendered window; this is the headless half of that contract.</summary>
public class AutomationIdsTests : UiTest
{
    private static readonly DateTime Mon7 = new(2026, 9, 7, 8, 0, 0);

    private static HashSet<string> Ids(Window window) =>
        window.GetVisualDescendants().OfType<Control>().Select(AutomationProperties.GetAutomationId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet()!;

    [AvaloniaFact]
    public async Task Shell_Schedule_And_Settings_Carry_Their_Ids()
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

        var ids = Ids(window);
        foreach (var id in new[] { "Win.Minimize", "Win.Maximize", "Win.Close", "Shell.SidebarToggle", "Shell.GroupCard", "Shell.ThemeToggle",
                                   "Nav.Schedule", "Nav.Week", "Nav.Summary", "Nav.Teachers", "Nav.Maps", "Nav.Friends", "Nav.Homework", "Nav.Settings",
                                   "Schedule.Title", "Schedule.Prev", "Schedule.Next", "ScheduleSegment.0", "ScheduleSegment.2", "Lesson.Title", "Lesson.Rename", "Lesson.Homework", "Lesson.Map", "Lesson.Hw" })
            Assert.Contains(id, ids);

        shell.NavigateTo(SectionKey.Settings);
        await Waits.Until(() => ((Features.Preferences.SettingsViewModel)shell.Current!).GroupName == "А863С", "settings");
        Pump();
        ids = Ids(window);
        foreach (var id in new[] { "SettingsTheme.0", "SettingsLanguage.1", "Settings.CompactSidebar", "Settings.Animations", "Settings.ChangeGroup", "Settings.ParityInvert", "Settings.Refresh",
                                   "Settings.NotifyEnabled", "Settings.NotifyTime1", "Settings.SaveTimes", "Settings.TestNotification", "Settings.Export", "Settings.Import", "Settings.Qr", "Settings.LanSync",
                                   "Updates.AutoUpdate", "Updates.Status", "Updates.Check", "About.Version", "About.DataFolder" })
            Assert.Contains(id, ids);

        var segment = window.GetVisualDescendants().OfType<SegmentedControl>().First(s => s.Name == "SettingsTheme");
        var buttons = segment.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(new[] { "SettingsTheme.0", "SettingsTheme.1", "SettingsTheme.2" }, buttons.Select(AutomationProperties.GetAutomationId));
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
