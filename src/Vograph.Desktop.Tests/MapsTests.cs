using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Core.Services;
using Vograph.Desktop.Controls;
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
        await Waits.Until(() => vm.Image is not null);

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
        await Waits.Until(() => vm.Image is not null);

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
        await Waits.Until(() => vm.Image is not null);

        vm.BuildingIndex = 1; // УЛК
        Assert.Equal(5, vm.Floors.Count);
        vm.SelectFloorCommand.Execute(vm.Floors[2]);
        await Waits.Until(() => vm.Current is { Building: "УЛК", Floor: 3 });
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
        await Waits.Until(() => vm.Image is not null);

        await vm.DownloadAllCommand.ExecuteAsync(null);
        Assert.False(vm.IsDownloading);
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
        await Waits.Until(() => vm.Image is not null);

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
        await Waits.Until(() => vm.Image is not null);
        Assert.Equal("1 из 9 планов офлайн", vm.CacheStatus);

        files.ThrowOnStatus = true;
        files.ThrowOnLocalPath = true;
        await vm.ActivateAsync(); // the shell runs this fire-and-forget: it must never throw

        Assert.Equal("1 из 9 планов офлайн", vm.CacheStatus); // the failed probe keeps the last known text
        Assert.Equal("План не загружен: нет сети и встроенной копии", vm.ImageError);
        Assert.Null(vm.Image);
    }

    /// <summary>T6 #4: after «Скачать свежие планы» the plan that failed to load before is shown again — with its
    /// highlight, not as a bare picture. The room 320 (УЛК 3) is the fixture's only one with coordinates.</summary>
    [AvaloniaFact]
    public async Task Download_All_Restores_The_Plan_With_Its_Highlight()
    {
        using var db = TestDb.Create();
        var (_, vm, files, _) = Make(db);
        files.EnsureFails = true;
        await vm.ShowLessonMapAsync(db.Services.Maps.Resolve("320*;")!, "Физика");
        Assert.Null(vm.Image);
        Assert.Equal("План не загружен: нет сети и встроенной копии", vm.ImageError);

        files.EnsureFails = false;
        await vm.DownloadAllCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Image);
        Assert.Null(vm.ImageError);
        Assert.True(vm.HasHighlight);
        Assert.Equal("320", vm.HighlightLabel);
        Assert.Equal(MapMode.Lesson, vm.Mode); // tracking was not switched on behind the user's back
    }

    /// <summary>T6 #5: Core swallows per-file failures, so the toast compares the cache against the total.</summary>
    [AvaloniaFact]
    public async Task Partial_Download_Warns_Instead_Of_Celebrating()
    {
        using var db = TestDb.Create();
        var (shell, vm, files, _) = Make(db, ("ГК", 4));
        files.SkipOnDownload = ("УЛК", 5);
        shell.NavigateTo(SectionKey.Maps);
        await Waits.Until(() => vm.Image is not null);

        await vm.DownloadAllCommand.ExecuteAsync(null);

        Assert.Equal("8 из 9 планов офлайн", vm.CacheStatus);
        Assert.Contains(db.Services.Toasts.Items, t => t.Kind == ToastKind.Warn && t.Text == "Скачано 8 из 9 — часть планов недоступна");
        Assert.DoesNotContain(db.Services.Toasts.Items, t => t.Text.StartsWith("Планы скачаны"));
        Assert.False(vm.IsDownloading);
    }

    /// <summary>T6 #8: the «ВЦ — показан план ГК» note is a localized string and follows the language switch.</summary>
    [AvaloniaFact]
    public async Task Vc_Note_Follows_The_Language()
    {
        using var db = TestDb.Create();
        var (_, vm, _, _) = Make(db);
        await vm.ShowLessonMapAsync(db.Services.Maps.Resolve("ВЦ 280;")!, "Матан");
        Assert.Equal("ВЦ — показан план ГК", vm.Note);
        db.Services.Loc.SetLanguage("en");
        try { Assert.Equal(db.Services.I18n.T("mapVc"), vm.Note); Assert.NotEqual("ВЦ — показан план ГК", vm.Note); }
        finally { db.Services.Loc.SetLanguage("ru"); }
    }

    /// <summary>T6 #15: a plan still decoding when the section is detached must not resurface as an undisposed bitmap.</summary>
    [AvaloniaFact]
    public async Task Detach_Wins_Over_An_In_Flight_Decode()
    {
        using var db = TestDb.Create();
        var (_, vm, _, _) = Make(db, ("ГК", 4));
        var showing = vm.ShowLessonMapAsync(db.Services.Maps.Resolve("493;")!, "Матан");
        vm.Detach();
        await showing;
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
        await Waits.Until(() => vm.Image is not null);
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

        // Ctrl+1…8 stay live over the fullscreen map (HandleShortcut only swallows the bare keys), so navigating
        // has to close it — otherwise the new section is switched to invisibly, behind the plan.
        vm.ToggleFullscreenCommand.Execute(null);
        Assert.True(shell.HasOverlay);
        shell.NavigateTo(SectionKey.Week);
        Assert.False(shell.HasOverlay);
    }

    [AvaloniaFact]
    public async Task No_Upcoming_Lessons_Is_Mode_None()
    {
        using var db = TestDb.Create();
        var s = db.Services.Db.GetSettings();
        s.MyGroupId = "9999";
        db.Services.Db.SaveSettings(s);
        var (shell, vm, _, _) = Make(db);
        shell.NavigateTo(SectionKey.Maps);
        await Waits.Until(() => vm.Mode == MapMode.None && vm.ContextLine.Length > 0, "maps none-mode");
        Assert.Equal("Нет предстоящих занятий", vm.ContextLine);
        Assert.False(vm.HasMap);
        Assert.Null(vm.Image);
        Assert.True(vm.ShowGoToNext); // «К следующей паре» stays available: pressing it re-checks the timetable
    }

    /// <summary>Detach() must unsubscribe the shell events, not merely block the decode: after switching the settings
    /// to a group without lessons, a live handler would re-track into Mode None («Нет предстоящих занятий»); a detached
    /// section keeps Mode NextLesson and its «Следующая пара …» line. Those are set before the _detached decode guard,
    /// so this cannot be satisfied by the guard alone.</summary>
    [AvaloniaFact]
    public async Task Detach_Ignores_Shell_Events()
    {
        using var db = TestDb.Create();
        var (shell, vm, _, _) = Make(db, ("ГК", 4));
        shell.NavigateTo(SectionKey.Maps);
        await Waits.Until(() => vm.Image is not null, "plan");
        Assert.Equal(MapMode.NextLesson, vm.Mode);
        var line = vm.ContextLine;
        Assert.StartsWith("Следующая пара · 493", line);

        vm.Detach();
        Assert.Null(vm.Image);

        var s = db.Services.Db.GetSettings();
        s.MyGroupId = "9999"; // Е452Б: no lessons — a live handler would now re-track into «Нет предстоящих занятий»
        db.Services.Db.SaveSettings(s);
        shell.RaiseScheduleChanged();
        shell.RaiseGroupChanged();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(MapMode.NextLesson, vm.Mode);
        Assert.Equal(line, vm.ContextLine);
        Assert.Null(vm.Image);
    }

    /// <summary>R29: with a dialog over the fullscreen map, Escape closes the dialog first, the map second.</summary>
    [AvaloniaFact]
    public async Task Escape_Closes_The_Dialog_Before_The_Fullscreen_Map()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var (shell, vm, _, _) = Make(db, ("ГК", 4));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        window.Focus();
        shell.NavigateTo(SectionKey.Maps);
        await Waits.Until(() => vm.Image is not null, "plan");
        vm.ToggleFullscreenCommand.Execute(null);
        var dialog = shell.Dialogs.ShowAsync(new Dialogs.ConfirmDialogViewModel("t", "m", "ok", false));
        Pump();

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(await dialog);
        Assert.True(shell.HasOverlay);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(shell.HasOverlay);
        AssertNoBindingErrors();
    }

    /// <summary>The fixture's only room with coordinates (coords.json: «УЛК 3» → «320»); the frame must show the highlight.</summary>
    [AvaloniaFact]
    public async Task Highlight_Is_Shown_For_A_Room_With_Coordinates()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var (shell, vm, _, _) = Make(db, ("УЛК", 3));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.ShowMap(db.Services.Maps.Resolve("320*;"), "Физика");
        await Waits.Until(() => vm.Image is not null, "plan");
        Pump();

        Assert.True(vm.HasHighlight);
        Assert.Equal("320", vm.HighlightLabel);
        Assert.True(vm.HighlightWidth > 0 && vm.HighlightHeight > 0);
        SetTheme(ThemeVariant.Light);
        Frames.Capture(window, "maps-highlight-light");
        SetTheme(ThemeVariant.Dark);
        Frames.Capture(window, "maps-highlight-dark");
        var label = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "HighlightLabel");
        Assert.True(label.IsVisible);
        Assert.False(label.GetVisualAncestors().OfType<ZoomPanel>().Any()); // outside the zoom transform: constant size
        AssertNoBindingErrors();
    }

    /// <summary>The chip lives outside the zoom transform, so only the code-behind moves it: pan and zoom the plan
    /// and it must land where MapsComposer.LabelOffset says. And it must get there without a layout pass — a label
    /// positioned through Margin invalidates its own measure and arrange, and a changed desired size then drags the
    /// map panel (with the ZoomPanel's arrange and transform) through a full layout pass per pan frame (T10-R6).</summary>
    [AvaloniaFact]
    public async Task Highlight_Label_Follows_The_View_Without_A_Layout_Pass()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var (shell, vm, _, _) = Make(db, ("УЛК", 3));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.ShowMap(db.Services.Maps.Resolve("320*;"), "Физика");
        await Waits.Until(() => vm.Image is not null, "plan");
        Pump();

        var zoom = window.GetVisualDescendants().OfType<ZoomPanel>().Single();
        var label = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "HighlightLabel");
        var host = (Visual)label.GetVisualParent()!;
        Assert.Null(zoom.Transitions); // motion is off in the suite, so every view change below lands at once

        Point Rendered() => label.TranslatePoint(new Point(0, 0), host)!.Value;
        Point Expected() => MapsComposer.LabelOffset(zoom.Scale, zoom.OffsetX, zoom.OffsetY, vm.HighlightLeft, vm.HighlightTop,
            zoom.Bounds.Size, label.Bounds.Size);
        void AssertTracks()
        {
            var (expected, rendered) = (Expected(), Rendered());
            Assert.Equal(expected.X, rendered.X, 3);
            Assert.Equal(expected.Y, rendered.Y, 3);
        }

        AssertTracks();
        var before = Rendered();

        zoom.Scale = 2;      // zoom in…
        zoom.OffsetX -= 40;  // …and pan
        zoom.OffsetY -= 25;
        Assert.True(label.IsMeasureValid, "moving the label must not invalidate its measure");
        Assert.True(label.IsArrangeValid, "…nor its arrange: that is a layout pass on every frame of a pan");

        Pump();
        Assert.NotEqual(before, Rendered());
        AssertTracks();
        AssertNoBindingErrors();
    }

    /// <summary>Viewport 600×400 (the card the plan is clipped to) with a 40×22 room chip in every row.</summary>
    [Theory]
    [InlineData(1.0, 0, 0, 100, 60, 100, 34)]
    [InlineData(2.0, 10, 20, 100, 60, 210, 114)]
    [InlineData(0.5, -300, 0, 100, 10, 0, 0)]     // off the left/top edge: clamped so the label stays readable
    [InlineData(1.0, 900, 0, 100, 60, 560, 34)]   // panned right: the chip stays inside the clipped card
    [InlineData(1.0, 0, 700, 100, 60, 100, 378)]  // panned down: same at the far edge
    public void Highlight_Label_Sits_Above_The_Rectangle_In_Viewport_Space(double scale, double ox, double oy, double left, double top, double x, double y) =>
        Assert.Equal(new Point(x, y), MapsComposer.LabelOffset(scale, ox, oy, left, top, new Size(600, 400), new Size(40, 22)));
}
