using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Campus;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Maps;

public sealed record FloorPill(int Floor, string Label, bool IsSelected);

public sealed record EntranceItem(string Id, string Label, bool IsSelected);

public sealed record RouteStepItem(string Text, string Building, int Floor, bool IsSelected = false);

public sealed partial class MapsViewModel : ViewModelBase
{
    private static readonly string[] Buildings = { "ГК", "УЛК" };
    private readonly ShellViewModel _shell;
    private readonly Func<DateTime> _clock;
    private readonly Action _onChange;
    private int _version;
    private int _stackVersion;
    private string? _lessonName;
    private DateTime? _start, _end;
    private CoordsRect? _coords;
    private bool _detached;
    private Route? _route;
    private readonly CampusGraph _graph;
    private string? _lastEntranceId;
    private string? _destRoomKey;
    private string? _prevRoomKey;
    private string? _fallbackToastKey;

    public MapsViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null, CampusGraph? graph = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _segmentItems = Buildings;
        _floors = MapsComposer.Floors("ГК").Select(f => new FloorPill(f, T("mapFloorN", f), false)).ToList();
        _cacheStatus = "";
        _graph = graph ?? LoadBundledGraph();
        LoadLastEntrance();
        RefreshEntrances();
        _onChange = () => { if (IsTracking) _ = TrackNextAsync(); };
        shell.GroupChanged += _onChange;
        shell.ScheduleChanged += _onChange;
        app.Loc.LanguageChanged += Relabel;
    }

    public override void Detach()
    {
        _detached = true;
        _shell.GroupChanged -= _onChange;
        _shell.ScheduleChanged -= _onChange;
        App.Loc.LanguageChanged -= Relabel;
        SetImage(null); // the section is going away: release the decode with it
        SetStackFloorImages(new Dictionary<int, Bitmap>());
    }

    /// <summary>◉ on a lesson hands over a map (and the name the card showed) through the shell; otherwise the
    /// section follows the next lesson. Called fire-and-forget by the shell, so nothing in here may throw.</summary>
    public override async Task ActivateAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        await RefreshCacheStatusAsync();
        if (_shell.TakePendingMap() is ({ } pending, var lessonName)) await ShowLessonMapAsync(pending, lessonName);
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
    [ObservableProperty] private IReadOnlyList<RouteStepItem> _routeSteps = [];
    [ObservableProperty] private IReadOnlyList<EntranceItem> _entrances = [];
    [ObservableProperty] private IReadOnlyList<Point> _pathPoints = [];
    [ObservableProperty] private IReadOnlyList<IReadOnlyList<Point>> _pathStrokes = [];
    [ObservableProperty] private IReadOnlyList<StairMarker> _stairMarkers = [];
    [ObservableProperty] private bool _hasPath;
    [ObservableProperty] private bool _showStack;
    [ObservableProperty] private IReadOnlyDictionary<int, Bitmap> _stackFloorImages = new Dictionary<int, Bitmap>();
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public bool HasMap => Current is { HasMap: true };
    public bool ShowGoToNext => Mode != MapMode.NextLesson;
    public bool HasRouteSteps => RouteSteps.Count > 0;
    public bool HasEntrances => Entrances.Count > 0;
    public bool IsRouteUnmarked => _route is null;
    public string RouteUnmarked => T("routeUnmarked");
    public Route? Route => _route;
    public string ShownBuilding
    {
        get
        {
            var b = Current?.Building ?? Buildings[Math.Clamp(BuildingIndex, 0, 1)];
            return b == "ВЦ" ? "ГК" : b;
        }
    }
    public bool ShowPlan => HasMap && !ShowStack;
    public bool ShowEmpty => !HasMap && !ShowStack;
    public bool ShowHighlightChrome => HasHighlight && !ShowStack;

    partial void OnModeChanged(MapMode value)
    {
        OnPropertyChanged(nameof(IsTracking));
        OnPropertyChanged(nameof(ShowGoToNext));
    }

    partial void OnNoteChanged(string? value) => OnPropertyChanged(nameof(HasNote));
    partial void OnCurrentChanged(MapInfo? value)
    {
        OnPropertyChanged(nameof(HasMap));
        OnPropertyChanged(nameof(ShownBuilding));
        OnPropertyChanged(nameof(ShowPlan));
        OnPropertyChanged(nameof(ShowEmpty));
    }
    partial void OnHasHighlightChanged(bool value) => OnPropertyChanged(nameof(ShowHighlightChrome));
    partial void OnShowStackChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPlan));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowHighlightChrome));
        if (value) _ = RefreshStackFloorsAsync();
    }
    partial void OnRouteStepsChanged(IReadOnlyList<RouteStepItem> value) => OnPropertyChanged(nameof(HasRouteSteps));
    partial void OnEntrancesChanged(IReadOnlyList<EntranceItem> value) => OnPropertyChanged(nameof(HasEntrances));

    partial void OnBuildingIndexChanged(int value)
    {
        var building = Buildings[Math.Clamp(value, 0, 1)];
        var selected = Current is { } c && (c.Building == "ВЦ" ? "ГК" : c.Building) == building ? c.Floor : 0;
        Floors = MapsComposer.Floors(building).Select(f => new FloorPill(f, T("mapFloorN", f), f == selected)).ToList();
        OnPropertyChanged(nameof(ShownBuilding));
        _ = RefreshStackFloorsAsync();
    }

    private sealed record NextData(Lesson? Lesson, DateTime Date, MapInfo? Map, string? Name, CoordsRect? Coords, string? DestRoomKey, string? PrevRoomKey);

    [RelayCommand]
    private Task GoToNext() => TrackNextAsync();

    public async Task TrackNextAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var version = ++_version;
        var now = _clock();
        var data = await RunAsync(() =>
        {
            var s = App.Db.GetSettings();
            if (string.IsNullOrEmpty(s.MyGroupId)) return new NextData(null, now, null, null, null, null, null);
            var (lesson, date) = App.Maps.GetNextLesson(s.MyGroupId, now);
            if (lesson is null) return new NextData(null, now, null, null, null, null, null);
            var map = App.Maps.GetMapForLesson(lesson);
            if (map is not null) AlignWithGraph(map);
            var name = LessonText.StripType(App.Overrides.GetDisplayName(lesson.SubjectRaw, lesson.DayOfWeek), lesson.TypeRaw);
            var prev = MapsComposer.PreviousLessonToday(App.Schedule.GetSchedule(now.Date, s.MyGroupId), lesson, now, date);
            return new NextData(
                lesson, date, map, name,
                map is { HasMap: true } ? App.Maps.GetCoords(map.Building == "ВЦ" ? "ГК" : map.Building, map.Floor, map.RoomRaw) : null,
                lesson.ClassroomRaw,
                prev?.ClassroomRaw);
        }, "maps");
        if (data is null || version != _version || !operation.IsCurrent) return;
        if (data.Lesson is null || data.Map is null)
        {
            Mode = MapMode.None;
            _lessonName = null; _start = _end = null;
            SetRouteEnds(null, null);
            await ShowMapAsync(null, null);
            return;
        }
        _lessonName = data.Name;
        _start = data.Date.Date + (TimeSpan.TryParse(data.Lesson.TimeStart, out var ts) ? ts : TimeSpan.Zero);
        _end = data.Date.Date + (TimeSpan.TryParse(data.Lesson.TimeEnd, out var te) ? te : TimeSpan.Zero);
        Mode = MapMode.NextLesson;
        SetRouteEnds(data.DestRoomKey, data.PrevRoomKey);
        await ShowMapAsync(data.Map, data.Coords);
    }

    /// <summary>◉ on a lesson: show that lesson's plan (manual-like: tracking stops until «К следующей паре»).</summary>
    public async Task ShowLessonMapAsync(MapInfo map, string? lessonName)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var version = ++_version;
        AlignWithGraph(map);
        _lessonName = lessonName; _start = _end = null;
        var coords = map.HasMap ? await RunAsync(() => App.Maps.GetCoords(map.Building == "ВЦ" ? "ГК" : map.Building, map.Floor, map.RoomRaw) ?? new CoordsRect { w = -1 }, "maps") : null;
        if (version != _version || !operation.IsCurrent) return;
        Mode = MapMode.Lesson;
        SetRouteEnds(map.ClassroomRaw, null);
        await ShowMapAsync(map, coords is { w: > 0 } ? coords : null);
    }

    [RelayCommand]
    private async Task SelectFloor(FloorPill pill)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var version = ++_version;
        var building = Buildings[Math.Clamp(BuildingIndex, 0, 1)];
        var map = await RunAsync(() => App.Maps.GetAllMaps().First(m => m.Building == building && m.Floor == pill.Floor), "maps");
        if (map is null || version != _version) return;
        Mode = MapMode.Manual;
        _lessonName = null; _start = _end = null;
        await ShowMapAsync(map, null);
    }

    public void ApplyRoute(Route? route)
    {
        _route = route;
        OnPropertyChanged(nameof(IsRouteUnmarked));
        OnPropertyChanged(nameof(Route));
        RefreshRouteSteps();
        RefreshPath();
    }

    [RelayCommand]
    private void ToggleStack() => ShowStack = !ShowStack;

    public void SetRouteEnds(string? destRoomKey, string? previousRoomKey)
    {
        _destRoomKey = destRoomKey;
        _prevRoomKey = previousRoomKey;
        ComputeRoute();
    }

    [RelayCommand]
    private void SelectEntrance(EntranceItem? item)
    {
        if (item is null || CampusRouter.ResolveEntrance(_graph, item.Id) is null) return;
        _lastEntranceId = item.Id;
        SaveLastEntrance();
        RefreshEntrances();
        ComputeRoute();
    }

    [RelayCommand]
    private async Task SelectRouteStep(RouteStepItem? step)
    {
        if (step is null) return;
        var index = Array.IndexOf(Buildings, step.Building);
        if (index < 0) return;
        BuildingIndex = index;
        var pill = Floors.FirstOrDefault(f => f.Floor == step.Floor);
        if (pill is null) return;
        ShowStack = false;
        await SelectFloor(pill);
    }

    private async Task ShowMapAsync(MapInfo? map, CoordsRect? coords)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        _coords = coords;
        Current = map;
        Note = map is null ? null : NoteFor(map);
        ContextLine = MapsComposer.ContextLine(Mode, map, _lessonName, _start, _end, _clock(), App.Loc);
        var shownBuilding = map is null ? "ГК" : map.Building == "ВЦ" ? "ГК" : map.Building;
        var index = Array.IndexOf(Buildings, shownBuilding) is var i and >= 0 ? i : 0;
        if (BuildingIndex == index) OnBuildingIndexChanged(index); // same building: re-mark the floor without a second rebuild (T6 #7)
        else BuildingIndex = index;
        HasHighlight = false;
        ImageError = null;
        if (map is not { HasMap: true } || map.IsRemote)
        {
            SetImage(null);
            RefreshPath();
            await RefreshStackFloorsAsync();
            return;
        }

        string? path;
        try
        {
            // IMapFiles is file (and, for EnsureAsync, network) work: off the UI thread, and a profile we cannot
            // read must not escape into the shell's fire-and-forget ActivateAsync.
            path = await Task.Run(() => App.MapFiles.LocalPath(map)) ?? await Task.Run(() => App.MapFiles.EnsureAsync(map));
        }
        catch (Exception ex)
        {
            App.Log.Error("maps", ex);
            path = null; // unreadable cache: the same outcome as "no copy anywhere"
        }
        if (!operation.IsCurrent) return;
        if (path is null)
        {
            SetImage(null);
            ImageError = T("mapNoImage");
            RefreshPath();
            await RefreshStackFloorsAsync();
            return;
        }
        Bitmap bmp;
        try { bmp = await Task.Run(() => new Bitmap(path)); }
        catch (Exception ex)
        {
            App.Log.Error("map image", ex);
            SetImage(null);
            ImageError = T("mapNoImage");
            RefreshPath();
            await RefreshStackFloorsAsync();
            return;
        }
        if (_detached || !operation.IsCurrent || !ReferenceEquals(Current, map)) { bmp.Dispose(); return; }
        SetImage(bmp);
        if (MapsComposer.Highlight(coords, bmp.PixelSize) is { } r)
        {
            HighlightLeft = r.X; HighlightTop = r.Y; HighlightWidth = r.Width; HighlightHeight = r.Height;
            HighlightLabel = MapsComposer.RoomText(map);
            HasHighlight = true;
        }
        RefreshPath();
        await RefreshStackFloorsAsync();
        await RefreshCacheStatusAsync();
    }

    private async Task RefreshStackFloorsAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var version = ++_stackVersion;
        var building = ShownBuilding;
        Dictionary<int, Bitmap> decoded = [];
        try
        {
            decoded = await Task.Run(() =>
            {
                var paths = MapsComposer.FloorRasterPaths(building, App.Maps.GetAllMaps(), m => App.MapFiles.LocalPath(m));
                Dictionary<int, Bitmap> map = [];
                foreach (var (floor, path) in paths)
                {
                    try
                    {
                        var bmp = MapsComposer.DecodeStackThumb(path);
                        if (bmp is not null) map[floor] = bmp;
                    }
                    catch (Exception ex)
                    {
                        App.Log.Error("stack thumb", ex);
                    }
                }
                return map;
            });
        }
        catch (Exception ex)
        {
            App.Log.Error("stack floors", ex);
        }
        if (_detached || !operation.IsCurrent || version != _stackVersion)
        {
            foreach (var bmp in decoded.Values) bmp.Dispose();
            return;
        }
        SetStackFloorImages(decoded);
    }

    private MapInfo AlignWithGraph(MapInfo map)
    {
        var node = CampusRouter.ResolveClassroom(_graph, map.ClassroomRaw);
        if (node is null) return map;
        map.Floor = node.Floor;
        map.RoomRaw = node.Room ?? map.RoomRaw;
        if (map.Building != "ВЦ") map.Building = node.Building;
        return map;
    }

    private void SetStackFloorImages(IReadOnlyDictionary<int, Bitmap> fresh)
    {
        var old = StackFloorImages;
        if (ReferenceEquals(old, fresh)) return;
        StackFloorImages = fresh;
        foreach (var bmp in old.Values) bmp.Dispose();
    }

    private void RefreshRouteSteps()
    {
        if (_route is null)
        {
            RouteSteps = [];
            return;
        }
        var building = ShownBuilding;
        var floor = Current?.Floor ?? 0;
        RouteSteps = Vograph.Core.Campus.RouteSteps.FormatWithLocations(_route, App.Loc.I18n)
            .Select(step => new RouteStepItem(step.Text, step.Building, step.Floor,
                string.Equals(step.Building, building, StringComparison.Ordinal) && step.Floor == floor)).ToArray();
    }

    private void RefreshPath()
    {
        StairMarkers = Current is { } shown
            ? MapsComposer.StairMarkers(_route, shown.Building == "ВЦ" ? "ГК" : shown.Building, shown.Floor)
            : [];
        IReadOnlyList<Point> points = [];
        IReadOnlyList<IReadOnlyList<Point>> strokes = [];
        if (_route is not null && Current is { } map && Image is { } bmp)
        {
            var building = map.Building == "ВЦ" ? "ГК" : map.Building;
            var size = bmp.PixelSize;
            var built = new List<IReadOnlyList<Point>>();
            var flat = new List<Point>();
            foreach (var stroke in MapsComposer.FloorPathStrokes(_route, building, map.Floor))
            {
                var px = MapsComposer.PathPixels(stroke, size).ToList();
                if (px.Count == 0) continue;
                built.Add(px);
                flat.AddRange(px);
            }
            strokes = built;
            points = flat;
        }
        PathStrokes = strokes;
        PathPoints = points;
        HasPath = strokes.Any(s => s.Count >= 2);
        RefreshRouteSteps();
    }

    private void ComputeRoute()
    {
        if (string.IsNullOrEmpty(_destRoomKey))
        {
            ApplyRoute(null);
            return;
        }
        var dest = CampusRouter.ResolveClassroom(_graph, _destRoomKey);
        var guessed = CampusRouter.ResolveClassroom(_graph, _prevRoomKey)
            ?? CampusRouter.ResolveEntrance(_graph, _lastEntranceId);
        var from = MapsComposer.StartFor(_graph, guessed?.Id, dest?.Id) ?? guessed;
        if (from is null || dest is null)
        {
            ApplyRoute(null);
            NotifyStartFallback(guessed, from);
            return;
        }
        var result = CampusRouter.Find(_graph, from.Id, dest.Id);
        ApplyRoute(result.Ok ? result.Route : null);
        NotifyStartFallback(guessed, from);
    }

    private void NotifyStartFallback(Node? requested, Node? chosen)
    {
        var message = MapsComposer.StartFallbackMessage(requested, chosen, App.Loc.I18n);
        if (message is null)
        {
            _fallbackToastKey = null;
            return;
        }
        if (_fallbackToastKey == message) return;
        _fallbackToastKey = message;
        App.Toasts.Info(message);
    }

    private void RefreshEntrances()
    {
        var items = new List<EntranceItem>();
        foreach (var node in CampusRouter.Entrances(_graph))
        {
            var label = string.IsNullOrWhiteSpace(node.Label) ? node.Id : node.Label!;
            items.Add(new EntranceItem(node.Id, label, node.Id == _lastEntranceId));
        }
        Entrances = items;
    }

    private CampusGraph LoadBundledGraph()
    {
        try
        {
            var path = Path.Combine(App.Maps.BundledDir, "campus-graph.json");
            if (!File.Exists(path)) return EmptyGraph();
            return CampusGraph.Load(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            App.Log.Error("campus-graph", ex);
            return EmptyGraph();
        }
    }

    private static CampusGraph EmptyGraph() =>
        CampusGraph.Load("""{"version":1,"buildings":["ГК","УЛК"],"nodes":[],"edges":[]}""");

    private string LastEntrancePath => Path.Combine(App.MapFiles.CacheDir, "last-entrance.txt");

    private void LoadLastEntrance()
    {
        try
        {
            var path = LastEntrancePath;
            if (!File.Exists(path)) return;
            var id = File.ReadAllText(path).Trim();
            if (CampusRouter.ResolveEntrance(_graph, id) is not null)
                _lastEntranceId = id;
        }
        catch (Exception ex)
        {
            App.Log.Error("last-entrance", ex);
        }
    }

    private void SaveLastEntrance()
    {
        try
        {
            var path = LastEntrancePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, _lastEntranceId ?? "");
        }
        catch (Exception ex)
        {
            App.Log.Error("last-entrance", ex);
        }
    }

    private string? NoteFor(MapInfo map) => map.Building == "ВЦ" ? T("mapVc") : string.IsNullOrEmpty(map.Note) ? null : map.Note;

    /// <summary>Swap, then dispose: the plan on screen stays until the new one is assigned, and only then is the
    /// old decode released. Nulling first would drop the reference before Dispose ever saw it, so every plan the
    /// user opened held its Skia buffer until finalization — megabytes per switch on the real ~2000×1400 JPEGs.</summary>
    private void SetImage(Bitmap? fresh)
    {
        var old = Image;
        if (ReferenceEquals(old, fresh)) return;
        Image = fresh;
        old?.Dispose();
    }

    /// <summary>MapService.GetCacheStatus walks all nine plans (a CreateDirectory plus a File.Exists/FileInfo each),
    /// so the probe runs off the UI thread; a failing one keeps the last known text instead of taking the section down.</summary>
    private async Task RefreshCacheStatusAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        try
        {
            var (cached, total) = await Task.Run(() => App.MapFiles.CacheStatus());
            if (!operation.IsCurrent) return;
            CacheStatus = T("mapCacheStatus", cached, total);
        }
        catch (Exception ex)
        {
            App.Log.Error("maps", ex);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task DownloadAll()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        IsDownloading = true;
        App.Toasts.Info(T("mapDownloading"));
        try
        {
            await Task.Run(() => App.MapFiles.DownloadAllAsync(new Progress<string>(s => App.Log.Info($"maps: {s}"))));
            var (cached, total) = await Task.Run(() => App.MapFiles.CacheStatus());
            // Core swallows per-file failures: only the cache count says whether every plan really arrived (T6 #5).
            if (cached == total) App.Toasts.Ok(T("mapDownloaded", cached, total));
            else App.Toasts.Warn(T("mapDownloadPartial", cached, total));
            // A plan that could not be shown before may be here now: re-show it the way it was requested (T6 #4).
            if (Image is null)
            {
                if (IsTracking) await TrackNextAsync();
                else if (Current is { } c) await ShowMapAsync(c, _coords);
            }
            else await RefreshStackFloorsAsync();
        }
        catch (Exception ex)
        {
            App.Log.Error("maps download", ex);
            App.Toasts.Error($"{T("errorTitle")}: {ex.Message}");
        }
        finally
        {
            IsDownloading = false;
            await RefreshCacheStatusAsync();
        }
    }

    [RelayCommand] private Task OpenSite() => App.Launcher.OpenUrlAsync("https://voenmeh.ru/openmap/");
    [RelayCommand] private Task OpenFolder() => App.Launcher.OpenFolderAsync(App.MapFiles.CacheDir);

    [RelayCommand]
    private async Task Verify()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        await RefreshCacheStatusAsync();
        App.Toasts.Info(CacheStatus);
    }

    [RelayCommand]
    private void ToggleFullscreen() => _shell.Overlay = _shell.Overlay is MapFullscreenViewModel ? null : new MapFullscreenViewModel(App, this);

    /// <summary>Loc.LanguageChanged is a synchronous event, so the re-probe is fire-and-forget — allowed because
    /// RefreshCacheStatusAsync swallows and logs every failure and therefore cannot throw.</summary>
    private void Relabel()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(RouteUnmarked));
        Note = Current is { } c ? NoteFor(c) : null;
        ContextLine = MapsComposer.ContextLine(Mode, Current, _lessonName, _start, _end, _clock(), App.Loc);
        OnBuildingIndexChanged(BuildingIndex);
        RefreshRouteSteps();
        _ = RefreshCacheStatusAsync();
    }
}
