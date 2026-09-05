using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Vograph.Desktop.Features.States;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
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

    [AvaloniaFact]
    public void Starts_On_Schedule_And_Navigates_To_Placeholders()
    {
        var (db, shell) = Make();
        using (db)
        {
            Assert.Equal(SectionKey.Schedule, shell.CurrentKey);
            Assert.True(shell.MainSections[0].IsActive);

            shell.NavigateCommand.Execute("Week");

            Assert.Equal(SectionKey.Week, shell.CurrentKey);
            var placeholder = Assert.IsType<PlaceholderViewModel>(shell.Current);
            Assert.Equal("Неделя", placeholder.Title);
            Assert.True(shell.MainSections[1].IsActive);
            Assert.False(shell.MainSections[0].IsActive);
            Assert.Same(shell.Current, shell.Section<PlaceholderViewModel>(SectionKey.Week)); // cached instance
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
    public void Window_Renders_Both_Themes_Hotkeys_Navigate_And_Collapse_Persists()
    {
        var (db, shell) = Make();
        using (db)
        {
            var window = new MainWindow { DataContext = shell };
            window.Show();
            window.Focus();

            SetTheme(ThemeVariant.Dark);
            Frames.Capture(window, "shell-dark");
            SetTheme(ThemeVariant.Light);
            Frames.Capture(window, "shell-light");

            window.KeyPress(Key.D3, RawInputModifiers.Control, PhysicalKey.Digit3, null);
            Assert.Equal(SectionKey.Summary, shell.CurrentKey);

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
}
