using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Maps;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#28: desktop-карты — чип не противоречит себе, нет упрёка за неотмеченную аудиторию, тулбар в одну строку.</summary>
public class MapsParityTests : UiTest
{
    private static readonly Loc Ru = new(new Vograph.Core.Services.I18nService());
    private static readonly MapInfo Map = new() { Building = "ГК", Floor = 4, RoomRaw = "493", ClassroomRaw = "493;", HasMap = true };

    [Fact]
    public void Running_lesson_reads_now_not_next_and_now()
    {
        var start = new DateTime(2026, 9, 7, 9, 0, 0); var end = new DateTime(2026, 9, 7, 10, 35, 0);
        var line = MapsComposer.ContextLine(MapMode.NextLesson, Map, null, start, end, new DateTime(2026, 9, 7, 9, 30, 0), Ru);
        Assert.Equal("Сейчас · 493 · ГК, 4 этаж", line);
        Assert.DoesNotContain("Следующая", line);
        Assert.StartsWith("Следующая пара · 493", MapsComposer.ContextLine(MapMode.NextLesson, Map, null, start, end, new DateTime(2026, 9, 7, 8, 0, 0), Ru));
    }

    [Fact]
    public void Unmarked_room_message_is_neutral_and_names_the_opened_plan()
    {
        var text = MapsComposer.RoomNotMarked("268 (Фесто);", new MapInfo { Building = "УЛК", Floor = 2 }, Ru);
        Assert.Equal("Аудитория 268 (Фесто) на плане не отмечена — открыли УЛК, 2 этаж", text);
        Assert.DoesNotContain("Проверьте", text);
        Assert.Equal("Аудитория на плане не отмечена", MapsComposer.RoomNotMarked(null, null, Ru));
    }

    [AvaloniaFact]
    public async Task Toolbar_fits_one_line_at_1440()
    {
        var now = new DateTime(2026, 9, 14, 9, 30, 0);
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        db.Services.MapFiles = new FakeMapFiles(Path.Combine(db.Dir, "maps"), ("ГК", 4));
        var shell = new ShellViewModel(db.Services) { Clock = () => now };
        shell.Register(SectionKey.Maps, () => new MapsViewModel(db.Services, shell, () => now));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        window.Show();
        SetTheme(ThemeVariant.Light, db.Services.Theme);
        shell.NavigateTo(SectionKey.Maps);
        Pump(); await Task.Delay(500); Pump();
        var search = window.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText == "Найти аудиторию");
        var bar = Assert.IsType<WrapPanel>(search.Parent);
        var tops = bar.Children.Where(c => c.IsVisible).Select(c => c.Bounds.Y + c.Bounds.Height / 2).ToList();
        Assert.True(tops.Max() - tops.Min() < 12, $"toolbar {bar.Bounds.Width:0}: " + string.Join(", ", bar.Children.Where(c => c.IsVisible).Select(c => $"{c.GetType().Name}@{c.Bounds.X:0},{c.Bounds.Y:0} w{c.Bounds.Width:0}")));
        Assert.True(bar.Bounds.Height < 64, $"toolbar height {bar.Bounds.Height}");
        window.Close();
        AssertNoBindingErrors();
    }

    [AvaloniaFact]
    public async Task Lists_are_not_cut_by_the_header_scroller_and_sit_above_the_map()
    {
        // R2-04: «Места на этом этаже» и «Планы без сети» были внутри шапки (ScrollViewer MaxHeight=240) и обрезались наполовину.
        var now = new DateTime(2026, 9, 14, 9, 30, 0);
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        db.Services.MapFiles = new FakeMapFiles(Path.Combine(db.Dir, "maps"), ("ГК", 4));
        var shell = new ShellViewModel(db.Services) { Clock = () => now };
        shell.Register(SectionKey.Maps, () => new MapsViewModel(db.Services, shell, () => now));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        window.Show();
        SetTheme(ThemeVariant.Light, db.Services.Theme);
        shell.NavigateTo(SectionKey.Maps);
        Pump(); await Task.Delay(500); Pump();
        var maps = window.GetVisualDescendants().OfType<MapsView>().Single();
        var offline = maps.GetVisualDescendants().OfType<Expander>().Single(e => Equals(e.Header, "Планы без сети"));
        Assert.DoesNotContain(offline.GetVisualAncestors().TakeWhile(a => a != maps), a => a is ScrollViewer);
        var card = maps.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("card") && b.ClipToBounds);
        var listBottom = offline.TranslatePoint(new Point(0, offline.Bounds.Height), maps)!.Value.Y;
        var cardTop = card.TranslatePoint(new Point(0, 0), maps)!.Value.Y;
        Assert.True(listBottom <= cardTop, $"list bottom {listBottom:0} > map top {cardTop:0}");
        window.Close();
        AssertNoBindingErrors();
    }
}
