using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Features.Communities;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>Ревью r2, пункт 5: 1440×900 — «Чаты» в сайдбаре при предупреждении об устаревших данных и подзаголовок «Аккаунт».</summary>
public class SidebarSettingsFitTests : UiTest
{
    private static readonly DateTime Mon = new(2026, 10, 12, 8, 0, 0);

    private static async Task<(TestDb Db, ShellViewModel Shell, MainWindow Window)> Open(bool stale)
    {
        var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        if (stale) { var s = db.Services.Db.GetSettings(); s.LastFetchedAt = DateTime.UtcNow.AddDays(-10).ToString("o"); db.Services.Db.SaveSettings(s); }
        var shell = new ShellViewModel(db.Services) { Clock = () => Mon };
        shell.Register(SectionKey.Schedule, () => new ScheduleViewModel(db.Services, shell, () => Mon));
        shell.Register(SectionKey.Community, () => new CommunitiesViewModel(db.Services));
        shell.Register(SectionKey.Settings, () => new SettingsViewModel(db.Services, shell, () => Mon));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        window.Show();
        SetTheme(ThemeVariant.Light, db.Services.Theme);
        Pump(); await Task.Delay(200); Pump();
        return (db, shell, window);
    }

    private static T ById<T>(Visual root, string id) where T : Control => root.GetVisualDescendants().OfType<T>()
        .First(c => AutomationProperties.GetAutomationId(c) == id && c.IsEffectivelyVisible);

    [AvaloniaFact]
    public async Task Chats_stays_inside_the_sidebar_at_900px_while_the_stale_warning_shows()
    {
        var (db, shell, window) = await Open(stale: true);
        using var _ = db;
        Assert.True(shell.ShowStaleChip);
        var chats = ById<Control>(window, "Nav.Chat");
        var scroller = chats.GetVisualAncestors().OfType<ScrollViewer>().First();
        var bottom = chats.TranslatePoint(new Point(0, chats.Bounds.Height), scroller)!.Value.Y;
        Assert.True(bottom <= scroller.Bounds.Height + 0.5, $"«Чаты» кончается на {bottom:0} px, видимая область сайдбара — {scroller.Bounds.Height:0} px");
        // Предупреждение — одной строкой, полный текст в подсказке.
        var chip = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("stale") && b.IsEffectivelyVisible);
        var text = chip.GetVisualDescendants().OfType<TextBlock>().First();
        Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
        Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
        Assert.Equal(shell.StaleText, ToolTip.GetTip(chip) as string);
    }

    [AvaloniaFact]
    public async Task Account_subtitle_wraps_inside_its_row_instead_of_being_cut()
    {
        var (db, shell, window) = await Open(stale: false);
        using var _ = db;
        shell.NavigateTo(SectionKey.Settings);
        Pump(); await Task.Delay(300); Pump();
        var row = ById<Button>(window, "Settings.Category.Account");
        var status = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("caption"));
        status.Text = "Вход временно недоступен. Расписание, карты и домашка работают без аккаунта.";
        Pump(); await Task.Delay(100); Pump();
        Assert.Equal(TextWrapping.Wrap, status.TextWrapping);
        var right = status.TranslatePoint(new Point(status.Bounds.Width, 0), row)!.Value.X;
        Assert.True(right <= row.Bounds.Width + 0.5, $"подзаголовок до {right:0} px, строка — {row.Bounds.Width:0} px");
        status.Measure(new Size(status.Bounds.Width, double.PositiveInfinity));
        Assert.True(status.Bounds.Height + 0.5 >= status.DesiredSize.Height, "текст обрезан по высоте");
        Assert.True(status.Bounds.Height > 20, "длинный статус переносится на вторую строку");
    }
}
