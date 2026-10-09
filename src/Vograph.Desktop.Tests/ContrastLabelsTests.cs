using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#20: контраст в обеих темах, прошедшие пары без прозрачности, подписи гамбургера и темы.</summary>
public class ContrastLabelsTests : UiTest
{
    private static double Lum(Color c)
    {
        static double Ch(byte v) { var x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }
    private static double Ratio(Color a, Color b) { var (x, y) = (Lum(a), Lum(b)); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }

    private static double EffectiveOpacity(Visual v)
    {
        var o = v.Opacity;
        foreach (var a in v.GetVisualAncestors()) o *= a.Opacity;
        return o;
    }

    private static Color BackgroundOf(Visual v)
    {
        foreach (var a in v.GetVisualAncestors())
        {
            var brush = a switch { Border b => b.Background, Panel p => p.Background, Avalonia.Controls.Presenters.ContentPresenter c => c.Background, Avalonia.Controls.Primitives.TemplatedControl t => t.Background, _ => null };
            if (brush is ISolidColorBrush s && s.Color.A == 255 && brush.Opacity >= 1) return s.Color;
        }
        return Colors.White;
    }

    private static Color Resource(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var value), key);
        return ((ISolidColorBrush)value!).Color;
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task Past_lesson_is_marked_not_faded_and_all_its_text_passes_AA(string themeName)
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        var afterFirstLesson = new DateTime(2026, 9, 14, 12, 0, 0);
        var vm = new ScheduleViewModel(db.Services, shell, () => afterFirstLesson);
        shell.Register(SectionKey.Schedule, () => vm);
        shell.NavigateTo(SectionKey.Schedule);
        await vm.InitializeAsync();
        Assert.True(vm.Lessons[0].IsPast);

        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        window.Show();
        SetTheme(themeName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, db.Services.Theme);
        Pump();

        var pastCard = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("past") && b.Classes.Contains("card"));
        Assert.Equal(1.0, EffectiveOpacity(pastCard), 3);
        var mark = pastCard.GetVisualDescendants().OfType<Border>().Single(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == "Lesson.Past");
        Assert.True(mark.IsEffectivelyVisible);
        Assert.Equal("Прошла", mark.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.Equal(Resource("Brush.Surface"), ((ISolidColorBrush)pastCard.Background!).Color);

        var texts = pastCard.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text) && t.Foreground is ISolidColorBrush).ToList();
        Assert.NotEmpty(texts);
        foreach (var t in texts)
        {
            Assert.True(EffectiveOpacity(t) >= 0.99 || t.Text == "", $"«{t.Text}» opacity {EffectiveOpacity(t):0.00}");
            var fg = ((ISolidColorBrush)t.Foreground!).Color;
            if (fg.A < 255) continue;
            var ratio = Ratio(fg, BackgroundOf(t));
            Assert.True(ratio >= 4.5, $"{themeName}: «{t.Text}» {fg} on {BackgroundOf(t)} = {ratio:0.00}");
        }

        // Кнопки у прошедшей пары — вторичные (outline), а не бледная основная.
        var primary = pastCard.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("primary"));
        Assert.Equal(Colors.Transparent, ((ISolidColorBrush)primary.Background!).Color);
        Assert.Equal(Resource("Brush.Text1"), ((ISolidColorBrush)primary.Foreground!).Color);
        Assert.Equal(Resource("Brush.ControlBorder"), ((ISolidColorBrush)primary.BorderBrush!).Color);
        Assert.Equal(1.0, EffectiveOpacity(primary), 3);
        AssertNoBindingErrors();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Field_borders_reach_3_to_1_and_secondary_text_4_5_to_1(string themeName)
    {
        SetTheme(themeName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        var border = Resource("Brush.ControlBorder");
        foreach (var bg in new[] { "Brush.Canvas", "Brush.Surface", "Brush.Card", "Brush.CardHover", "Brush.Chip" })
            Assert.True(Ratio(border, Resource(bg)) >= 3, $"{themeName}: border on {bg} = {Ratio(border, Resource(bg)):0.00}");
        foreach (var bg in new[] { "Brush.Canvas", "Brush.Surface", "Brush.Card", "Brush.Chip" })
            Assert.True(Ratio(Resource("Brush.Text2"), Resource(bg)) >= 4.5, $"{themeName}: Text2 on {bg}");

        var box = new TextBox();
        var window = new Window { Content = box };
        window.Show(); Pump();
        Assert.Equal(border, ((ISolidColorBrush)box.BorderBrush!).Color);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Menu_and_theme_buttons_have_tooltips_and_screen_reader_names()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        window.Show();
        SetTheme(ThemeVariant.Light, db.Services.Theme);
        shell.IsDark = false; Pump();

        Button ById(string id) => window.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == id);
        var menu = ById("Shell.SidebarToggle");
        Assert.Equal("Свернуть меню", Avalonia.Automation.AutomationProperties.GetName(menu));
        Assert.Equal("Свернуть меню (Ctrl+B)", ToolTip.GetTip(menu));
        var theme = ById("Shell.ThemeToggle");
        Assert.Equal("Тёмная тема", Avalonia.Automation.AutomationProperties.GetName(theme));
        Assert.Equal("Тёмная тема", ToolTip.GetTip(theme));

        shell.IsDark = true; shell.SidebarCollapsed = true; Pump();
        Assert.Equal("Светлая тема", Avalonia.Automation.AutomationProperties.GetName(theme));
        Assert.Equal("Развернуть меню", Avalonia.Automation.AutomationProperties.GetName(ById("Shell.SidebarToggle")));
        AssertNoBindingErrors();
    }

    [AvaloniaFact]
    public void Product_name_is_the_same_in_window_title_title_bar_and_strings()
    {
        var window = new MainWindow();
        Assert.Equal("Военмех — расписание и карты", window.Title);
        Assert.Equal(window.Title, new Vograph.Core.Services.I18nService().T("appTitle"));
        Assert.Contains(window.GetLogicalDescendantsOrSelf().OfType<TextBlock>(), t => t.Text == "Военмех");
    }
}
