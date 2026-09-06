using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Features.Week;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class WeekTests : UiTest
{
    private static readonly DateTime Mon7 = new(2026, 9, 7, 8, 0, 0);   // odd week (Tue 01.09 .. Mon 07.09)
    private static readonly DateTime Wed9 = new(2026, 9, 9, 12, 0, 0);  // even week

    [Fact]
    public void Odd_Week_Rows_And_Nearest_Dates_Follow_The_Tue_To_Mon_Cycle()
    {
        using var db = TestDb.Create();
        var model = new WeekComposer(db.Services).Compose(1, Mon7);

        Assert.True(model.HasGroup);
        Assert.Equal(1, model.Parity);
        Assert.True(model.IsOddToday);
        Assert.Equal(6, model.Days.Count);
        Assert.Equal(new[] { 2, 1, 1, 0, 0, 1 }, model.Days.Select(d => d.Rows.Count));
        Assert.Equal(5, model.Total);
        // Monday is today; the other odd days lie in the NEXT odd week because weeks run Tue..Mon.
        Assert.Equal(new[] { "07.09", "15.09", "16.09", "17.09", "18.09", "19.09" }, model.Days.Select(d => d.Date.ToString("dd.MM")));
        Assert.True(model.Days[0].IsToday);
        Assert.All(model.Days.Skip(1), d => Assert.False(d.IsToday));
        Assert.Equal("Понедельник", model.Days[0].Title);

        var mon = model.Days[0].Rows;
        Assert.Equal(("09:00", "Матан", "лекция", "493 ГК"), (mon[0].Time, mon[0].Name, mon[0].TypeLabel, mon[0].Room));
        Assert.Equal(("12:40", "ОСН РОС ГОС", "практика", "563 УЛК"), (mon[1].Time, mon[1].Name, mon[1].TypeLabel, mon[1].Room));
        Assert.Equal("дистанционно", model.Days[5].Rows[0].Room);
    }

    [Fact]
    public void Even_Week_And_Parity_Inversion()
    {
        using var db = TestDb.Create();
        var composer = new WeekComposer(db.Services);

        var even = composer.Compose(2, Mon7);
        Assert.Equal(new[] { 1, 0, 1, 0, 0, 0 }, even.Days.Select(d => d.Rows.Count));
        Assert.Equal(new[] { "14.09", "08.09", "09.09", "10.09", "11.09", "12.09" }, even.Days.Select(d => d.Date.ToString("dd.MM")));
        Assert.Equal("ВЦ 280 ГК", even.Days[2].Rows[0].Room);

        var current = composer.Compose(0, Wed9); // 0 = whatever week today is
        Assert.Equal(2, current.Parity);
        Assert.False(current.IsOddToday);
        Assert.True(current.Days[2].IsToday);

        var s = db.Services.Db.GetSettings();
        s.ParityInvert = true;
        db.Services.Db.SaveSettings(s);
        var inverted = composer.Compose(1, Mon7); // "odd" as the user sees it now maps to the XML even week
        Assert.Equal(new[] { 1, 0, 1, 0, 0, 0 }, inverted.Days.Select(d => d.Rows.Count));
        Assert.False(inverted.IsOddToday);
    }

    [Fact]
    public void No_Group_Gives_An_Empty_Model()
    {
        using var db = TestDb.Create();
        var s = db.Services.Db.GetSettings();
        s.MyGroupId = "";
        db.Services.Db.SaveSettings(s);
        var model = new WeekComposer(db.Services).Compose(1, Mon7);
        Assert.False(model.HasGroup);
        Assert.Empty(model.Days);
    }

    [Fact]
    public async Task ViewModel_Starts_On_The_Current_Week_And_Switches()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new WeekViewModel(db.Services, shell, () => Wed9);

        await vm.ReloadAsync();

        Assert.Equal(1, vm.ParityIndex); // even week is current
        Assert.Equal("Нечетная", vm.SegmentItems[0]);
        Assert.Equal("Четная · текущая", vm.SegmentItems[1]);
        Assert.Equal(2, vm.Days.Sum(d => d.Rows.Count));
        Assert.Contains("2 пары", vm.Subtitle);

        vm.ParityIndex = 0;
        await vm.ReloadAsync(); // the property change already queued a reload; awaiting another one drains the gate in order
        Assert.Equal(5, vm.Days.Sum(d => d.Rows.Count));
        Assert.Equal("Нечетная", vm.SegmentItems[0]);
    }

    [AvaloniaFact]
    public async Task Week_Renders_Both_Themes_And_Day_Click_Opens_That_Date()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        shell.Register(SectionKey.Schedule, () => new ScheduleViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Week, () => new WeekViewModel(db.Services, shell, () => Mon7));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();

        shell.NavigateTo(SectionKey.Week);
        var week = Assert.IsType<WeekViewModel>(shell.Current);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (week.Days.Count < 6 && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        Pump();

        SetTheme(ThemeVariant.Dark);
        Frames.Capture(window, "week-dark");
        SetTheme(ThemeVariant.Light);
        Frames.Capture(window, "week-light");

        var cards = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("weekday")).ToList();
        Assert.Equal(6, cards.Count);
        Click(window, cards[2]); // Wednesday of the odd week → 16.09
        var schedule = Assert.IsType<ScheduleViewModel>(shell.Current);
        sw.Restart();
        while (schedule.Date != new DateTime(2026, 9, 16) && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(new DateTime(2026, 9, 16), schedule.Date);
        Assert.Equal(9, schedule.DayOffset);

        AssertNoBindingErrors();
    }
}
