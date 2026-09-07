using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>Stage-1 review debt (roadmap): rail, chrome glyphs, tooltips, hover actions, group picker, startup error window.</summary>
public class PolishTests : UiTest
{
    [Theory]
    [InlineData("А863С", "А8")]
    [InlineData("09С31", "09")]
    [InlineData("Е4", "Е4")]
    [InlineData(" О3313 ", "О3")]
    [InlineData("", "—")]
    [InlineData(null, "—")]
    public void Rail_Label_Is_The_First_Two_Characters(string? name, string expected) => Assert.Equal(expected, GroupCardLogic.RailLabel(name));

    [Fact]
    public void Rail_Shows_Initials_And_A_Stale_Dot_Instead_Of_The_Chip()
    {
        using var db = TestDb.Create();
        var s = db.Services.Db.GetSettings();
        s.LastFetchedAt = DateTime.UtcNow.AddDays(-10).ToString("o"); // older than 7 days → warn
        db.Services.Db.SaveSettings(s);
        var shell = new ShellViewModel(db.Services);
        Assert.Equal("А8", shell.GroupRailLabel);
        Assert.True(shell.HasStale);
        Assert.True(shell.StaleWarn);
        Assert.True(shell.ShowStaleChip);
        Assert.False(shell.ShowStaleDot);
        Assert.Equal(db.Services.Loc.T("myGroup"), shell.GroupCardTip); // expanded: the plain label

        shell.SidebarCollapsed = true;
        Assert.False(shell.ShowStaleChip);
        Assert.True(shell.ShowStaleDot);
        Assert.StartsWith("А863С", shell.GroupCardTip);
        Assert.Contains("обновлено", shell.GroupCardTip); // the rail tooltip carries what the chip would have said
        Assert.Equal("Развернуть панель (Ctrl+B)", shell.SidebarToggleTip);
        shell.SidebarCollapsed = false;
        Assert.Equal("Свернуть панель (Ctrl+B)", shell.SidebarToggleTip);
    }

    [Fact]
    public void Nav_Tooltips_Only_On_The_Rail_And_With_The_Hotkey()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var week = shell.MainSections[1];
        Assert.Equal("Ctrl+2", week.Hotkey);
        Assert.Equal("Nav.Week", week.AutomationId);
        Assert.Null(week.Tip);
        shell.SidebarCollapsed = true;
        Assert.Equal("Неделя (Ctrl+2)", week.Tip);
        Assert.Equal("Настройки (Ctrl+8)", shell.SettingsSection.Tip);
        db.Services.Loc.SetLanguage("en");
        try { Assert.Equal("Week (Ctrl+2)", week.Tip); }
        finally { db.Services.Loc.SetLanguage("ru"); }
    }

    [AvaloniaFact]
    public void Theme_Button_Shows_The_Sun_In_The_Dark()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        db.Services.Theme.Apply(ThemeChoice.Dark, save: false);
        var shell = new ShellViewModel(db.Services);
        Assert.True(shell.IsDark);
        var sun = (Geometry)Application.Current!.FindResource("Icon.Sun")!;
        var moon = (Geometry)Application.Current!.FindResource("Icon.Moon")!;
        Assert.Same(sun, Converters.ThemeIcon.Convert(true, typeof(Geometry), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Same(moon, Converters.ThemeIcon.Convert(false, typeof(Geometry), null, System.Globalization.CultureInfo.InvariantCulture));
        shell.ToggleThemeCommand.Execute(null);
        Assert.False(shell.IsDark);
    }

    [AvaloniaFact]
    public void Maximize_Button_Shows_Restore_When_Maximized()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Pump();
        Assert.False(shell.IsMaximized);
        Assert.Equal("Развернуть", shell.MaximizeTip);
        window.WindowState = WindowState.Maximized;
        Pump();
        Assert.True(shell.IsMaximized);
        Assert.Equal("Свернуть в окно", shell.MaximizeTip);
        var restore = (Geometry)Application.Current!.FindResource("Icon.Restore")!;
        Assert.Same(restore, Converters.MaximizeIcon.Convert(true, typeof(Geometry), null, System.Globalization.CultureInfo.InvariantCulture));
        window.WindowState = WindowState.Normal;
        Pump();
        Assert.False(shell.IsMaximized);
        AssertNoBindingErrors();
    }

    [AvaloniaFact]
    public async Task Hover_Actions_Are_Hidden_Not_Just_Transparent()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        var vm = new ScheduleViewModel(db.Services, shell, () => new DateTime(2026, 9, 7, 8, 0, 0));
        shell.Register(SectionKey.Schedule, () => vm);
        shell.NavigateTo(SectionKey.Schedule);
        await vm.InitializeAsync();
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Pump();

        var acts = window.GetVisualDescendants().OfType<StackPanel>().Where(p => p.Classes.Contains("acts")).ToList();
        Assert.Equal(2, acts.Count);
        Assert.All(acts, a => Assert.False(a.IsVisible)); // invisible controls take no Tab focus

        var card = window.GetVisualDescendants().OfType<LessonCardView>().First();
        var centre = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), window)!.Value;
        window.MouseMove(centre);
        Pump();
        Assert.True(acts[0].IsVisible);
        Assert.False(acts[1].IsVisible);
        AssertNoBindingErrors();
    }

    [AvaloniaFact]
    public void Startup_Error_Window_Renders()
    {
        var window = new StartupErrorWindow { DataContext = new StartupError("SQLite Error 14: 'unable to open database file'", @"C:\Users\x\AppData\Local\Vograph", @"C:\Users\x\AppData\Local\Vograph\logs\startup-error.log") };
        window.Show();
        Pump();
        SetTheme(ThemeVariant.Dark);
        Frames.Capture(window, "startup-error-dark");
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("SQLite Error 14") == true);
        AssertNoBindingErrors();
    }

    [Fact]
    public void Startup_Error_Is_Written_To_A_Log_File()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = App.WriteStartupError(new InvalidOperationException("boom"), dir);
            Assert.True(File.Exists(path));
            Assert.Contains("boom", File.ReadAllText(path));
            Assert.StartsWith(Path.Combine(dir, "logs"), path);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
