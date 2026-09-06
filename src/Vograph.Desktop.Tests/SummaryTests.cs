using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Features.Summary;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class SummaryTests : UiTest
{
    private static readonly DateTime Mon7 = new(2026, 9, 7, 8, 0, 0); // odd

    [Fact]
    public void Odd_Even_And_Both_Aggregate_The_Fixture()
    {
        using var db = TestDb.Create();
        var composer = new SummaryComposer(db.Services);

        var odd = composer.Compose(1, Mon7);
        Assert.True(odd.HasGroup);
        Assert.Equal(5, odd.Total);
        Assert.Equal(new[] { ("Пн", 2), ("Вт", 1), ("Ср", 1), ("Чт", 0), ("Пт", 0), ("Сб", 1) }, odd.ByDay.Select(c => (c.Name, c.Count)));
        Assert.Equal(new[] { ("лекция", 3), ("практика", 2) }, odd.ByType.Select(c => (c.Name, c.Count)));
        Assert.Equal(5, odd.Subjects.Count);
        Assert.Contains(odd.Subjects, c => c.Name == "Матан" && c.Count == 1);      // renamed, type stripped
        Assert.Contains(odd.Subjects, c => c.Name == "ОСН РОС ГОС" && c.Count == 1);
        Assert.Equal(4, odd.Teachers.Count);                                          // the Tuesday lesson has no teacher
        Assert.Equal(new[] { "493", "563*", "526*", "дистанционно" }.OrderBy(r => r), odd.Rooms.Select(r => r.Name).OrderBy(r => r));

        var even = composer.Compose(2, Mon7);
        Assert.Equal(2, even.Total);
        Assert.Equal(new[] { ("ВЫСШ. МАТЕМАТ", 1), ("Матан", 1) }, even.Subjects.Select(c => (c.Name, c.Count))); // lecture renamed, practice not

        var both = composer.Compose(0, Mon7);
        Assert.Equal(7, both.Total);
        Assert.Equal(new[] { 3, 1, 2, 0, 0, 1 }, both.ByDay.Select(c => c.Count));
        Assert.Equal(("Матан", 2), (both.Subjects[0].Name, both.Subjects[0].Count));  // most frequent first
        Assert.Equal(("Барт Е.Л.", 2), (both.Teachers[0].Name, both.Teachers[0].Count));
        Assert.Equal(("493", 2), (both.Rooms[0].Name, both.Rooms[0].Count));

        var current = composer.Compose(null, Mon7);
        Assert.Equal(1, current.Parity);
        Assert.True(current.IsOddToday);
    }

    [Fact]
    public void Inversion_Swaps_Which_Xml_Week_Is_Odd()
    {
        using var db = TestDb.Create();
        var s = db.Services.Db.GetSettings();
        s.ParityInvert = true;
        db.Services.Db.SaveSettings(s);
        var odd = new SummaryComposer(db.Services).Compose(1, Mon7);
        Assert.Equal(2, odd.Total); // the user's "odd" is now the XML even week
    }

    [Fact]
    public async Task ViewModel_Segments_And_Subtitle()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new SummaryViewModel(db.Services, shell, () => Mon7);

        await vm.ReloadAsync();
        Assert.Equal(0, vm.SegmentIndex);
        Assert.Equal(new[] { "Нечетная", "Четная", "Обе" }, vm.SegmentItems);
        Assert.Equal("5", vm.TotalText);
        Assert.Equal(6, vm.DayBars.Count);
        Assert.Equal(40, vm.DayBars[0].Height);   // the busiest day fills the bar
        Assert.Equal(20, vm.DayBars[1].Height);
        Assert.Equal(0, vm.DayBars[3].Height);

        vm.SegmentIndex = 2;
        await vm.ReloadAsync();
        Assert.Equal("7", vm.TotalText);
        Assert.Contains("7 пар", vm.Subtitle);
    }

    [AvaloniaFact]
    public async Task Summary_Renders_Both_Themes_And_Segment_Click_Switches()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        shell.Register(SectionKey.Summary, () => new SummaryViewModel(db.Services, shell, () => Mon7));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();

        shell.NavigateTo(SectionKey.Summary);
        var vm = Assert.IsType<SummaryViewModel>(shell.Current);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (vm.TotalText != "5" && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        Pump();
        SetTheme(ThemeVariant.Dark);
        Frames.Capture(window, "summary-dark");
        SetTheme(ThemeVariant.Light);
        Frames.Capture(window, "summary-light");

        var seg = window.GetVisualDescendants().OfType<SegmentedControl>().Single();
        Click(window, seg.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Last()); // «Обе»
        sw.Restart();
        while (vm.TotalText != "7" && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal("7", vm.TotalText);
        AssertNoBindingErrors();
    }
}
