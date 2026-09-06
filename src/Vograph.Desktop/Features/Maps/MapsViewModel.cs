using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Maps;

public sealed record FloorPill(int Floor, string Label, bool IsSelected);

public sealed partial class MapsViewModel : ViewModelBase
{
    private static readonly string[] Buildings = { "ГК", "УЛК" };
    private readonly ShellViewModel _shell;
    private readonly Func<DateTime> _clock;
    private readonly Action _onChange;
    private int _version;
    private string? _lessonName;
    private DateTime? _start, _end;

    public MapsViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _segmentItems = Buildings;
        _floors = MapsComposer.Floors("ГК").Select(f => new FloorPill(f, T("mapFloorN", f), false)).ToList();
        _cacheStatus = "";
        _onChange = () => { if (IsTracking) _ = TrackNextAsync(); };
        shell.GroupChanged += _onChange;
        shell.ScheduleChanged += _onChange;
        app.Loc.LanguageChanged += Relabel;
    }

    public override void Detach()
    {
        _shell.GroupChanged -= _onChange;
        _shell.ScheduleChanged -= _onChange;
        App.Loc.LanguageChanged -= Relabel;
    }

    /// <summary>◉ on a lesson hands over a map through the shell; otherwise the section follows the next lesson.</summary>
    public override async Task ActivateAsync()
    {
        RefreshCacheStatus();
        if (_shell.TakePendingMap() is { } pending) await ShowLessonMapAsync(pending, null);
        else if (Mode is MapMode.None or MapMode.NextLesson) await TrackNextAsync();
    }

    public string Title => T("navMaps");
    public bool IsTracking => Mode == MapMode.NextLesson;

    [ObservableProperty] private MapMode _mode;
    [ObservableProperty] private string _contextLine = "";
    [ObservableProperty] private IList<string> _segmentItems;
    [ObservableProperty] private int _buildingIndex;
    [ObservableProperty] private IReadOnlyList<FloorPill> _floors;
    [ObservableProperty] private MapInfo? _current;
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string? _imageError;
    [ObservableProperty] private bool _hasHighlight;
    [ObservableProperty] private double _highlightLeft, _highlightTop, _highlightWidth, _highlightHeight;
    [ObservableProperty] private string? _highlightLabel;
    [ObservableProperty] private string? _note;
    [ObservableProperty] private string _cacheStatus;
    [ObservableProperty] private bool _isDownloading;
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public bool HasMap => Current is { HasMap: true };
    public bool ShowGoToNext => Mode != MapMode.NextLesson;

    partial void OnModeChanged(MapMode value)
    {
        OnPropertyChanged(nameof(IsTracking));
        OnPropertyChanged(nameof(ShowGoToNext));
    }

    partial void OnNoteChanged(string? value) => OnPropertyChanged(nameof(HasNote));
    partial void OnCurrentChanged(MapInfo? value) => OnPropertyChanged(nameof(HasMap));

    partial void OnBuildingIndexChanged(int value)
    {
        var building = Buildings[Math.Clamp(value, 0, 1)];
        var selected = Current is { } c && (c.Building == "ВЦ" ? "ГК" : c.Building) == building ? c.Floor : 0;
        Floors = MapsComposer.Floors(building).Select(f => new FloorPill(f, T("mapFloorN", f), f == selected)).ToList();
    }

    private sealed record NextData(Lesson? Lesson, DateTime Date, MapInfo? Map, string? Name, CoordsRect? Coords);

    [RelayCommand]
    private Task GoToNext() => TrackNextAsync();

    public async Task TrackNextAsync()
    {
        var version = ++_version;
        var now = _clock();
        var data = await RunAsync(() =>
        {
            var s = App.Db.GetSettings();
            if (string.IsNullOrEmpty(s.MyGroupId)) return new NextData(null, now, null, null, null);
            var (lesson, date) = App.Maps.GetNextLesson(s.MyGroupId, now);
            if (lesson is null) return new NextData(null, now, null, null, null);
            var map = App.Maps.GetMapForLesson(lesson);
            var name = ScheduleComposer.StripType(App.Overrides.GetDisplayName(lesson.SubjectRaw, lesson.DayOfWeek), lesson.TypeRaw);
            return new NextData(lesson, date, map, name, map is { HasMap: true } ? App.Maps.GetCoords(map.Building == "ВЦ" ? "ГК" : map.Building, map.Floor, map.RoomRaw) : null);
        }, "maps");
        if (data is null || version != _version) return;
        if (data.Lesson is null || data.Map is null)
        {
            Mode = MapMode.None;
            _lessonName = null; _start = _end = null;
            await ShowMapAsync(null, null);
            return;
        }
        _lessonName = data.Name;
        _start = data.Date.Date + (TimeSpan.TryParse(data.Lesson.TimeStart, out var ts) ? ts : TimeSpan.Zero);
        _end = data.Date.Date + (TimeSpan.TryParse(data.Lesson.TimeEnd, out var te) ? te : TimeSpan.Zero);
        Mode = MapMode.NextLesson;
        await ShowMapAsync(data.Map, data.Coords);
    }

    /// <summary>◉ on a lesson: show that lesson's plan (manual-like: tracking stops until «К следующей паре»).</summary>
    public async Task ShowLessonMapAsync(MapInfo map, string? lessonName)
    {
        var version = ++_version;
        _lessonName = lessonName; _start = _end = null;
        var coords = map.HasMap ? await RunAsync(() => App.Maps.GetCoords(map.Building == "ВЦ" ? "ГК" : map.Building, map.Floor, map.RoomRaw) ?? new CoordsRect { w = -1 }, "maps") : null;
        if (version != _version) return;
        Mode = MapMode.Lesson;
        await ShowMapAsync(map, coords is { w: > 0 } ? coords : null);
    }

    [RelayCommand]
    private async Task SelectFloor(FloorPill pill)
    {
        var version = ++_version;
        var building = Buildings[Math.Clamp(BuildingIndex, 0, 1)];
        var map = await RunAsync(() => App.Maps.GetAllMaps().First(m => m.Building == building && m.Floor == pill.Floor), "maps");
        if (map is null || version != _version) return;
        Mode = MapMode.Manual;
        _lessonName = null; _start = _end = null;
        await ShowMapAsync(map, null);
    }

    private async Task ShowMapAsync(MapInfo? map, CoordsRect? coords)
    {
        Current = map;
        Note = map is null ? null : map.Building == "ВЦ" ? T("mapVc") : string.IsNullOrEmpty(map.Note) ? null : map.Note;
        ContextLine = MapsComposer.ContextLine(Mode, map, _lessonName, _start, _end, _clock(), App.Loc);
        var shownBuilding = map is null ? "ГК" : map.Building == "ВЦ" ? "ГК" : map.Building;
        BuildingIndex = Array.IndexOf(Buildings, shownBuilding) is var i and >= 0 ? i : 0;
        OnBuildingIndexChanged(BuildingIndex); // re-mark the selected floor even when the index did not change
        HasHighlight = false;
        Image = null;
        ImageError = null;
        if (map is not { HasMap: true } || map.IsRemote) return;

        var path = App.MapFiles.LocalPath(map) ?? await Task.Run(() => App.MapFiles.EnsureAsync(map));
        if (path is null)
        {
            ImageError = T("mapNoImage");
            return;
        }
        Bitmap bmp;
        try { bmp = await Task.Run(() => new Bitmap(path)); }
        catch (Exception ex)
        {
            App.Log.Error("map image", ex);
            ImageError = T("mapNoImage");
            return;
        }
        if (!ReferenceEquals(Current, map)) { bmp.Dispose(); return; } // superseded while decoding
        Image?.Dispose();
        Image = bmp;
        if (MapsComposer.Highlight(coords, bmp.PixelSize) is { } r)
        {
            HighlightLeft = r.X; HighlightTop = r.Y; HighlightWidth = r.Width; HighlightHeight = r.Height;
            HighlightLabel = MapsComposer.RoomText(map);
            HasHighlight = true;
        }
        RefreshCacheStatus();
    }

    private void RefreshCacheStatus()
    {
        var (cached, total) = App.MapFiles.CacheStatus();
        CacheStatus = T("mapCacheStatus", cached, total);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task DownloadAll()
    {
        IsDownloading = true;
        App.Toasts.Info(T("mapDownloading"));
        try
        {
            await Task.Run(() => App.MapFiles.DownloadAllAsync(new Progress<string>(s => App.Log.Info($"maps: {s}"))));
            var (cached, total) = App.MapFiles.CacheStatus();
            App.Toasts.Ok(T("mapDownloaded", cached, total));
            if (Current is { } c && Image is null) await ShowMapAsync(c, null);
        }
        catch (Exception ex)
        {
            App.Log.Error("maps download", ex);
            App.Toasts.Error($"{T("errorTitle")}: {ex.Message}");
        }
        finally
        {
            IsDownloading = false;
            RefreshCacheStatus();
        }
    }

    [RelayCommand] private Task OpenSite() => App.Launcher.OpenUrlAsync("https://voenmeh.ru/openmap/");
    [RelayCommand] private Task OpenFolder() => App.Launcher.OpenFolderAsync(App.MapFiles.CacheDir);

    [RelayCommand]
    private Task Verify()
    {
        RefreshCacheStatus();
        App.Toasts.Info(CacheStatus);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ToggleFullscreen() => _shell.Overlay = _shell.Overlay is MapFullscreenViewModel ? null : new MapFullscreenViewModel(App, this);

    private void Relabel()
    {
        OnPropertyChanged(nameof(Title));
        ContextLine = MapsComposer.ContextLine(Mode, Current, _lessonName, _start, _end, _clock(), App.Loc);
        OnBuildingIndexChanged(BuildingIndex);
        RefreshCacheStatus();
    }
}
