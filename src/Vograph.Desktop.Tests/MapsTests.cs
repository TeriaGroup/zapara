using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Maps;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class MapsTests : UiTest
{
    private static readonly DateTime Mon8 = new(2026, 9, 7, 8, 0, 0);
    private static readonly Loc Ru = new(new I18nService("ru"));

    private static (ShellViewModel Shell, MapsViewModel Vm, FakeMapFiles Files, FakeLauncher Launcher) Make(TestDb db, params (string, int)[] cached)
    {
        var files = new FakeMapFiles(Path.Combine(db.Dir, "maps"), cached);
        var launcher = new FakeLauncher();
        db.Services.MapFiles = files;
        db.Services.Launcher = launcher;
        var shell = new ShellViewModel(db.Services);
        var vm = new MapsViewModel(db.Services, shell, () => Mon8);
        shell.Register(SectionKey.Maps, () => vm);
        return (shell, vm, files, launcher);
    }

    private static async Task WaitAsync(Func<bool> done)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(done(), "condition not met in time");
    }

    [Theory]
    [InlineData("2026-09-07T08:00", "2026-09-07T09:00", "2026-09-07T10:35", "через 1 ч")]
    [InlineData("2026-09-07T08:35", "2026-09-07T09:00", "2026-09-07T10:35", "через 25 мин")]
    [InlineData("2026-09-07T09:30", "2026-09-07T09:00", "2026-09-07T10:35", "идёт сейчас")]
    [InlineData("2026-09-06T14:00", "2026-09-07T09:00", "2026-09-07T10:35", "через 19 ч")]
    [InlineData("2026-09-05T08:00", "2026-09-07T09:00", "2026-09-07T10:35", "через 2 дн.")]
    public void Until_Formats_Minutes_Hours_Days(string now, string start, string end, string expected) =>
        Assert.Equal(expected, MapsComposer.Until(DateTime.Parse(now), DateTime.Parse(start), DateTime.Parse(end), Ru));

    [Fact]
    public void Context_Lines_Floors_And_Highlight()
    {
        using var db = TestDb.Create();
        var map = db.Services.Maps.Resolve("493;")!;
        Assert.Equal("Следующая пара · 493 · ГК, 4 этаж · через 1 ч",
            MapsComposer.ContextLine(MapMode.NextLesson, map, null, new DateTime(2026, 9, 7, 9, 0, 0), new DateTime(2026, 9, 7, 10, 35, 0), Mon8, Ru));
        Assert.Equal("Пара: Матан · 493 · ГК, 4 этаж", MapsComposer.ContextLine(MapMode.Lesson, map, "Матан", null, null, Mon8, Ru));
        Assert.Equal("Выберите план", MapsComposer.ContextLine(MapMode.Manual, map, null, null, null, Mon8, Ru));
        Assert.Equal("Нет предстоящих занятий", MapsComposer.ContextLine(MapMode.None, null, null, null, null, Mon8, Ru));
        Assert.Equal(new[] { 1, 2, 3, 4 }, MapsComposer.Floors("ГК"));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, MapsComposer.Floors("УЛК"));
        var rect = MapsComposer.Highlight(new CoordsRect { x = 0.5, y = 0.25, w = 0.1, h = 0.2 }, new PixelSize(1000, 800));
        Assert.Equal(new Rect(500, 200, 100, 160), rect);
        Assert.Null(MapsComposer.Highlight(null, new PixelSize(1000, 800)));
    }

    [AvaloniaFact]
    public async Task Auto_Mode_Tracks_The_Next_Lesson_And_Loads_The_Plan()
    {
        using var db = TestDb.Create();
        var (shell, vm, files, _) = Make(db, ("ГК", 4));
        shell.NavigateTo(SectionKey.Maps);
        await WaitAsync(() => vm.Image is not null);

        Assert.Equal(MapMode.NextLesson, vm.Mode);
        Assert.True(vm.IsTracking);
        Assert.Equal(("ГК", 4), (vm.Current!.Building, vm.Current.Floor));
        Assert.StartsWith("Следующая пара · 493 · ГК, 4 этаж · через 1 ч", vm.ContextLine);
        Assert.Equal(0, vm.BuildingIndex);
        Assert.Equal(4, vm.Floors.Count);
        Assert.True(vm.Floors[3].IsSelected);
        Assert.Equal(new PixelSize(200, 100), vm.Image!.PixelSize);
        Assert.Equal(0, files.EnsureCalls); // cached: no download
        Assert.Equal("1 из 9 планов офлайн", vm.CacheStatus);
    }

    [AvaloniaFact]
    public async Task Card_Action_Opens_The_Lesson_Map_With_A_Note_For_VC()
    {
        using var db = TestDb.Create();
        var (shell, vm, files, _) = Make(db);
        var map = db.Services.Maps.Resolve("ВЦ 280;")!;

        shell.ShowMap(map); // ◉ on a lesson card
        await WaitAsync(() => vm.Image is not null);

        Assert.Equal(SectionKey.Maps, shell.CurrentKey);
        Assert.Equal(MapMode.Lesson, vm.Mode);
        Assert.False(vm.IsTracking);
        Assert.Equal(("ВЦ", 2), (vm.Current!.Building, vm.Current.Floor));
        Assert.Equal("ВЦ — показан план ГК", vm.Note);
        Assert.StartsWith("Пара: ", vm.ContextLine);
        Assert.Equal(1, files.EnsureCalls);   // not cached: fetched once
        Assert.Null(shell.PendingMap);        // consumed
    }

    [AvaloniaFact]
    public async Task Manual_Selection_Stops_Tracking_Until_Go_To_Next()
    {
        using var db = TestDb.Create();
        var (shell, vm, _, _) = Make(db, ("ГК", 4), ("УЛК", 3));
        shell.NavigateTo(SectionKey.Maps);
        await WaitAsync(() => vm.Image is not null);

        vm.BuildingIndex = 1; // УЛК
        Assert.Equal(5, vm.Floors.Count);
        vm.SelectFloorCommand.Execute(vm.Floors[2]);
        await WaitAsync(() => vm.Current is { Building: "УЛК", Floor: 3 });
        Assert.Equal(MapMode.Manual, vm.Mode);
        Assert.False(vm.IsTracking);
        Assert.Equal("Выберите план", vm.ContextLine);
        Assert.False(vm.HasHighlight);

        await vm.GoToNextCommand.ExecuteAsync(null);
        Assert.Equal(MapMode.NextLesson, vm.Mode);
        Assert.Equal(("ГК", 4), (vm.Current!.Building, vm.Current.Floor));
    }

    [AvaloniaFact]
    public async Task Download_All_And_Menu_Actions()
    {
        using var db = TestDb.Create();
        var (shell, vm, files, launcher) = Make(db, ("ГК", 4));
        shell.NavigateTo(SectionKey.Maps);
        await WaitAsync(() => vm.Image is not null);

        await vm.DownloadAllCommand.ExecuteAsync(null);
        Assert.Equal(9, files.Progress.Count);
        Assert.Equal("9 из 9 планов офлайн", vm.CacheStatus);
        Assert.Contains(db.Services.Toasts.Items, t => t.Text == "Планы скачаны: 9 из 9");

        await vm.OpenSiteCommand.ExecuteAsync(null);
        Assert.Equal("https://voenmeh.ru/openmap/", Assert.Single(launcher.Urls));
        await vm.OpenFolderCommand.ExecuteAsync(null);
        Assert.Equal(files.CacheDir, Assert.Single(launcher.Folders));
        await vm.VerifyCommand.ExecuteAsync(null);
        Assert.Contains(db.Services.Toasts.Items, t => t.Text == "9 из 9 планов офлайн");
    }

    [AvaloniaFact]
    public async Task Lesson_Handover_Names_The_Lesson_In_The_Header()
    {
        using var db = TestDb.Create();
        var (shell, vm, _, _) = Make(db, ("ГК", 4));
        var map = db.Services.Maps.Resolve("493;")!;

        shell.ShowMap(map, "Матан"); // ◉ on a lesson card, with the name the card shows
        await WaitAsync(() => vm.Image is not null);

        Assert.Equal(MapMode.Lesson, vm.Mode);
        Assert.StartsWith("Пара: Матан · 493", vm.ContextLine);
        Assert.Null(shell.PendingLessonName); // consumed together with the map
    }

    [Fact]
    public async Task Lesson_Card_Hands_Its_Display_Name_To_The_Shell()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        shell.Register(SectionKey.Maps, () => new Features.States.LoadingViewModel(db.Services)); // nothing consumes the handover
        var schedule = new Features.Schedule.ScheduleViewModel(db.Services, shell, () => Mon8);
        shell.Register(SectionKey.Schedule, () => schedule);
        await schedule.InitializeAsync();
        var row = schedule.Lessons.First(r => r.CanShowMap);
        Assert.Equal("Матан", row.DisplayName); // renamed and stripped of the type, exactly as the card shows it

        row.ShowMapCommand.Execute(null); // ◉ on the card

        Assert.Equal(SectionKey.Maps, shell.CurrentKey);
        var (map, lessonName) = shell.TakePendingMap();
        Assert.Same(row.Row.Map, map);
        Assert.Equal(row.DisplayName, lessonName);
        Assert.Null(shell.PendingMap);
        Assert.Null(shell.PendingLessonName);
    }

    [AvaloniaFact]
    public async Task Switching_Plans_Disposes_The_Previous_Decode()
    {
        using var db = TestDb.Create();
        var (_, vm, _, _) = Make(db, ("ГК", 4), ("УЛК", 5));

        await vm.ShowLessonMapAsync(db.Services.Maps.Resolve("493;")!, "Матан");
        var first = vm.Image!;
        await vm.ShowLessonMapAsync(db.Services.Maps.Resolve("526*;")!, "История");
        var second = vm.Image!;

        Assert.NotSame(first, second);
        Assert.Equal(new PixelSize(200, 100), second.PixelSize);
        Assert.Throws<ObjectDisposedException>(() => _ = first.PixelSize);

        vm.Detach();
        Assert.Null(vm.Image);
        Assert.Throws<ObjectDisposedException>(() => _ = second.PixelSize);
    }

    [AvaloniaFact]
    public async Task Cache_Probe_Failures_Never_Escape_The_Section()
    {
        using var db = TestDb.Create();
        var (shell, vm, files, _) = Make(db, ("ГК", 4));
        shell.NavigateTo(SectionKey.Maps);
        await WaitAsync(() => vm.Image is not null);
        Assert.Equal("1 из 9 планов офлайн", vm.CacheStatus);

        files.ThrowOnStatus = true;
        files.ThrowOnLocalPath = true;
        await vm.ActivateAsync(); // the shell runs this fire-and-forget: it must never throw

        Assert.Equal("1 из 9 планов офлайн", vm.CacheStatus); // the failed probe keeps the last known text
        Assert.Equal("План не загружен: нет сети и встроенной копии", vm.ImageError);
        Assert.Null(vm.Image);
    }

    [AvaloniaFact]
    public async Task Renders_Both_Themes_And_Fullscreen_Closes_On_Escape()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var (shell, vm, _, _) = Make(db, ("ГК", 4));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.NavigateTo(SectionKey.Maps);
        await WaitAsync(() => vm.Image is not null);
        Pump();

        SetTheme(ThemeVariant.Dark);
        Frames.Capture(window, "maps-dark");
        SetTheme(ThemeVariant.Light);
        Frames.Capture(window, "maps-light");

        vm.ToggleFullscreenCommand.Execute(null);
        Assert.IsType<MapFullscreenViewModel>(shell.Overlay);
        Pump();
        Frames.Capture(window, "maps-fullscreen-light");
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Null(shell.Overlay);

        // Swapping the plan disposes the one the window is rendering: the renderer holds its own ref, so this is safe.
        var shown = vm.Image!;
        await vm.ShowLessonMapAsync(db.Services.Maps.Resolve("526*;")!, "История");
        Pump();
        Frames.Capture(window, "maps-swap-light");
        Assert.NotSame(shown, vm.Image);
        Assert.Throws<ObjectDisposedException>(() => _ = shown.PixelSize);
        AssertNoBindingErrors();
    }
}
