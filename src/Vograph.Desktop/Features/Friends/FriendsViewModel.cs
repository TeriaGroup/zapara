using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Friends;

public sealed record ColorOption(int Index, bool IsCurrent);
public sealed record FriendEncounterViewModel(string When, string GroupName, string Members, string Place, int ColorIndex)
{
    public string GroupDisplay => string.IsNullOrWhiteSpace(Members) ? GroupName : $"{GroupName} · {Members}";
}

public sealed partial class FriendsViewModel : ViewModelBase
{
    private const int MaxFriends = 5;
    private readonly ShellViewModel _shell;
    private readonly Func<DateTime> _clock;
    private readonly Action _reload;
    private readonly DispatcherTimer _forecastTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool _suppress;
    private bool _suppressReload;
    private int _version;
    private Task? _pendingSettingsSave;

    public FriendsViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _tickLabels = BuildTicks();
        _strictnessLabel = LabelFor(25);
        _reload = () => { if (!_suppressReload) _ = LoadAsync(); };
        _forecastTimer.Tick += OnForecastTick;
        shell.GroupChanged += _reload;
        shell.ScheduleChanged += _reload;
        app.Loc.LanguageChanged += _reload;
    }

    public override void Detach()
    {
        _forecastTimer.Stop();
        _forecastTimer.Tick -= OnForecastTick;
        _shell.GroupChanged -= _reload;
        _shell.ScheduleChanged -= _reload;
        App.Loc.LanguageChanged -= _reload;
    }

    public override Task ActivateAsync()
    {
        _forecastTimer.Start();
        return LoadAsync();
    }

    private void OnForecastTick(object? sender, EventArgs e) => _ = RefreshPreviewAsync();

    public string Title => T("navFriends");
    public string Subtitle => T("friendsSubtitle");
    public ObservableCollection<FriendItemViewModel> Friends { get; } = new();

    [ObservableProperty] private bool _canAdd = true;
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private double _strictness = 25;
    [ObservableProperty] private string _strictnessLabel;
    [ObservableProperty] private IList<string> _tickLabels;
    [ObservableProperty] private bool _alwaysShowAll;
    [ObservableProperty] private string _previewLine = "";
    [ObservableProperty] private IReadOnlyList<FriendMarkViewModel> _previewMarks = Array.Empty<FriendMarkViewModel>();
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private IReadOnlyList<FriendEncounterViewModel> _encounters = Array.Empty<FriendEncounterViewModel>();
    [ObservableProperty] private string _forecastStatus = "";
    [ObservableProperty] private string _missingSchedulesText = "";

    // Short tick labels for the slider (strictTick25..100) are their own keys, distinct from the long
    // inter25..100 texts the schedule's dot tooltips use ("в том же корпусе" etc. would not fit under a tick).
    private IList<string> BuildTicks() => new[] { T("strictTick25"), T("strictTick50"), T("strictTick75"), T("strictTick100") };
    private string LabelFor(double v) => T(v >= 100 ? "strictTick100" : v >= 75 ? "strictTick75" : v >= 50 ? "strictTick50" : "strictTick25");
    public int StrictnessIndex
    {
        get => Strictness >= 100 ? 3 : Strictness >= 75 ? 2 : Strictness >= 50 ? 1 : 0;
        set
        {
            if (value is < 0 or > 3) return;
            Strictness = 25 + value * 25;
        }
    }

    partial void OnStrictnessChanged(double value)
    {
        StrictnessLabel = LabelFor(value);
        OnPropertyChanged(nameof(StrictnessIndex));
        if (!_suppress) _pendingSettingsSave = SaveSettingsAsync();
    }

    partial void OnAlwaysShowAllChanged(bool value)
    {
        if (!_suppress) _pendingSettingsSave = SaveSettingsAsync();
    }

    private sealed record PreviewData(string Line, IReadOnlyList<FriendMark> Marks,
        IReadOnlyList<FriendEncounterViewModel> Encounters, string Status, string Missing);
    private sealed record FriendsData(List<FriendGroup> Friends, Settings Settings, PreviewData? Preview);

    public async Task LoadAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var version = ++_version;
        var now = _clock();
        var data = await RunAsync(() =>
        {
            var friends = App.Db.GetFriends();
            var settings = App.Db.GetSettings();
            return new FriendsData(friends, settings, ComputePreview(friends, settings, now));
        }, "friends");
        if (data is null || version != _version || !operation.IsCurrent) return;
        _suppress = true;
        Strictness = Math.Clamp(data.Settings.IntersectionStrictness, 25, 100);
        AlwaysShowAll = data.Settings.AlwaysShowAllTrafficLights;
        _suppress = false;
        TickLabels = BuildTicks();
        SyncFriends(data.Friends);
        RefreshColorOptions();
        CanAdd = Friends.Count < MaxFriends;
        CountText = T("friendsCount", Friends.Count, MaxFriends);
        ApplyPreview(data.Preview);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
    }

    /// <summary>Up to three upcoming schedule overlaps. A loaded empty timetable differs from an unavailable one.</summary>
    private PreviewData ComputePreview(List<FriendGroup> friends, Settings settings, DateTime now)
    {
        var loc = App.Loc;
        var myId = settings.MyGroupId ?? "";
        var enabled = friends.Where(f => f.Enabled).Take(MaxFriends).ToList();
        var catalog = App.Db.GetAllGroups();
        var cache = new TimetableApiCache(App.Db);
        bool Loaded(string id, Group? group) => cache.Read(id) is not null || group?.LastFetchedAt is not null || App.Db.GetAllLessonsForGroup(id).Count > 0;
        var ownGroup = catalog.FirstOrDefault(g => g.Id == myId);
        var ownLoaded = myId.Length > 0 && Loaded(myId, ownGroup);
        var ready = enabled.Select(friend =>
        {
            var group = catalog.FirstOrDefault(g => g.Name.Equals(friend.GroupName, StringComparison.OrdinalIgnoreCase) ||
                g.Id.Equals(friend.GroupName, StringComparison.OrdinalIgnoreCase));
            return (Friend: friend, Id: group?.Id ?? "", Available: group is not null && group.Id != myId && Loaded(group.Id, group) && cache.CanIntersect(myId, group.Id));
        }).ToList();
        var missing = ready.Where(item => !item.Available).Select(item => item.Friend.GroupName).ToList();
        var usable = ready.Where(item => item.Available).Select(item => item.Friend).ToList();
        var encounters = new List<FriendEncounterViewModel>();
        (string Line, IReadOnlyList<FriendMark> Marks)? first = null;
        (string Line, IReadOnlyList<FriendMark> Marks)? fallback = null;
        for (var i = 0; ownLoaded && i < 14 && encounters.Count < 3; i++)
        {
            var date = now.Date.AddDays(i);
            foreach (var l in App.Schedule.GetSchedule(date, myId).OrderBy(x => TimeSpan.TryParse(x.TimeStart, out var t) ? t : TimeSpan.Zero))
            {
                if (encounters.Count == 3) break;
                if (!TimeSpan.TryParse(l.TimeStart, out var start)) continue;
                var end = TimeSpan.TryParse(l.TimeEnd, out var parsedEnd) ? parsedEnd : start.Add(TimeSpan.FromMinutes(95));
                if (i == 0 && end <= now.TimeOfDay) continue;
                var marks = FriendMarks.Compute(App.Intersections, l, date, friends, settings, loc);
                var name = LessonText.StripType(App.Overrides.GetDisplayName(l.SubjectRaw, l.DayOfWeek), l.TypeRaw);
                var line = $"{loc.I18n.FormatDay(date)} {DayTitles.ShortDate(date, loc)} · {l.TimeStart} · {name}";
                if (marks.Count > 0) fallback ??= (line, marks);
                if (usable.Count == 0) continue;
                var hits = App.Intersections.GetIntersections(l, date, usable, strictness: 0);
                foreach (var friend in usable)
                {
                    var best = hits.Where(hit => hit.FriendGroupName == friend.GroupName).OrderByDescending(hit => hit.Score).FirstOrDefault();
                    if (best is null || best.Score < settings.IntersectionStrictness) continue;
                    first ??= (line, marks);
                    var place = loc.T(best.Score switch { >= 100 => "inter100", >= 75 => "inter75", >= 50 => "inter50", _ => "inter25" });
                    encounters.Add(new FriendEncounterViewModel(line, friend.GroupName, friend.MemberNames ?? "",
                        string.IsNullOrWhiteSpace(best.Room) ? place : $"{place} · {best.Room}", FriendPalette.IndexOf(friend.ColorHex)));
                    if (encounters.Count == 3) break;
                }
            }
        }
        var preview = first ?? fallback;
        var status = myId.Length == 0 ? "Выберите свою группу, чтобы увидеть пересечения."
            : !ownLoaded ? "Расписание вашей группы ещё не загружено."
            : enabled.Count == 0 ? "Включите группу друзей или добавьте новую."
            : usable.Count == 0 ? "Нет загруженных совместимых расписаний групп друзей."
            : encounters.Count == 0 ? T("previewNone") : "";
        var missingText = usable.Count == 0 || missing.Count == 0 ? "" :
            $"Нет загруженного совместимого расписания: {string.Join(", ", missing)}. Прогноз пока неполный.";
        return new PreviewData(preview?.Line ?? "", preview?.Marks ?? Array.Empty<FriendMark>(), encounters, status, missingText);
    }

    private void ApplyPreview(PreviewData? p)
    {
        HasPreview = !string.IsNullOrEmpty(p?.Line);
        PreviewLine = HasPreview ? p!.Line : T("previewNone");
        PreviewMarks = p is null ? Array.Empty<FriendMarkViewModel>() : p.Marks.Select(m => new FriendMarkViewModel(m)).ToList();
        Encounters = p?.Encounters ?? Array.Empty<FriendEncounterViewModel>();
        ForecastStatus = p?.Status ?? "";
        MissingSchedulesText = p?.Missing ?? "";
    }

    /// <summary>Called explicitly after Strictness/AlwaysShowAll change (and by Save/SetColor). Awaits the
    /// settings write and its owned reload before starting another preview read.</summary>
    public async Task RefreshPreviewAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        if (_pendingSettingsSave is { } pending) await pending;
        var version = ++_version;
        var now = _clock();
        var preview = await RunAsync(() => ComputePreview(App.Db.GetFriends(), App.Db.GetSettings(), now), "friends");
        if (preview is null || version != _version || !operation.IsCurrent) return;
        ApplyPreview(preview);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RefreshSchedules()
    {
        _suppressReload = true;
        try { await _shell.RefreshScheduleAsync(force: true, quiet: false); }
        finally { _suppressReload = false; }
        await LoadAsync();
    }

    private async Task SaveSettingsAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var strictness = (int)Math.Round(Strictness);
        var always = AlwaysShowAll;
        var ok = await RunAsync(() =>
        {
            var s = App.Db.GetSettings();
            s.IntersectionStrictness = strictness;
            s.AlwaysShowAllTrafficLights = always;
            App.Db.SaveSettings(s);
        }, "friends settings");
        if (!ok) return;
        // Own this reload instead of launching it through our fire-and-forget event handler.
        // Awaiters of the settings change must not return while that SQLite read is still queued.
        await LoadAsync();
        RaiseScheduleChangedQuietly();
    }

    /// <summary>Tell the schedule cards without reloading ourselves: callers here have just reloaded (T7 #6).</summary>
    private void RaiseScheduleChangedQuietly()
    {
        _suppressReload = true;
        try { _shell.RaiseScheduleChanged(); }
        finally { _suppressReload = false; }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Add()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        if (!CanAdd) return;
        var taken = Friends.Select(f => f.GroupName).ToHashSet(StringComparer.OrdinalIgnoreCase); // T7 #7: same group number, different case
        var groups = await RunAsync(() =>
        {
            var my = App.Db.GetSettings().MyGroupId;
            return App.Db.GetAllGroups().Where(g => g.Id != my && !taken.Contains(g.Name)).ToList();
        }, "groups");
        if (groups is null) return;
        var dlg = new GroupPickerDialogViewModel(groups, null);
        if (!await _shell.Dialogs.ShowAsync(dlg) || dlg.Selected is null) return;
        var name = dlg.Selected.Name;
        var color = FriendPalette.Hex[FirstFreeColor()];
        var ok = await RunAsync(() => App.Db.InsertFriend(new FriendGroup { GroupName = name, ColorHex = color, Enabled = true, MemberNames = "" }), "friend add");
        if (!ok) return;
        await _shell.EnsureApiNeedsAsync();
        await LoadAsync();
        RaiseScheduleChangedQuietly();
        App.Toasts.Ok(T("friendAdded", name));
    }

    private int FirstFreeColor()
    {
        var used = Friends.Select(f => f.ColorIndex).ToHashSet();
        for (var i = 0; i < FriendPalette.Hex.Length; i++) if (!used.Contains(i)) return i;
        return 0;
    }

    public async Task RemoveAsync(FriendItemViewModel item)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var confirm = new ConfirmDialogViewModel(T("friendsRemove"), T("friendsRemoveConfirm", item.GroupName), T("delete"), danger: true);
        if (!await _shell.Dialogs.ShowAsync(confirm)) return;
        if (!await RunAsync(() => { App.Db.DeleteFriend(item.Model.Id); App.Api.Invalidate(); }, "friend delete")) return;
        await _shell.EnsureApiNeedsAsync();
        await LoadAsync();
        RaiseScheduleChangedQuietly();
    }

    /// <summary>Names / enabled flag. The row is written from a snapshot: a reload landing between the edit and the
    /// gated write may swap item.Model, and the edit must not be lost or half-applied.</summary>
    public async Task SaveAsync(FriendItemViewModel item)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var model = item.Model;
        var copy = new FriendGroup { Id = model.Id, GroupName = model.GroupName, ColorHex = model.ColorHex, MemberNames = item.MemberNames, Enabled = item.Enabled };
        if (!await RunAsync(() => { App.Db.UpdateFriend(copy); App.Api.Invalidate(); }, "friend save")) return;
        await _shell.EnsureApiNeedsAsync();
        if (!operation.IsCurrent) return;
        model.MemberNames = copy.MemberNames;
        model.Enabled = copy.Enabled;
        RaiseScheduleChangedQuietly();
        await RefreshPreviewAsync();
    }

    public async Task SetColorAsync(FriendItemViewModel item, int index)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        if (index < 0 || index >= FriendPalette.Hex.Length) return;
        if (Friends.Any(f => !ReferenceEquals(f, item) && f.ColorIndex == index)) return; // taken
        var model = item.Model;
        var copy = new FriendGroup { Id = model.Id, GroupName = model.GroupName, ColorHex = FriendPalette.Hex[index], MemberNames = model.MemberNames, Enabled = model.Enabled };
        if (!await RunAsync(() => App.Db.UpdateFriend(copy), "friend color")) return;
        model.ColorHex = copy.ColorHex;
        item.ColorIndex = index;
        RefreshColorOptions();
        RaiseScheduleChangedQuietly();
        await RefreshPreviewAsync();
    }

    private void RefreshColorOptions()
    {
        foreach (var f in Friends)
        {
            var taken = Friends.Where(o => !ReferenceEquals(o, f)).Select(o => o.ColorIndex).ToHashSet();
            f.ColorOptions = Enumerable.Range(0, FriendPalette.Hex.Length).Where(i => !taken.Contains(i)).Select(i => new ColorOption(i, i == f.ColorIndex)).ToList();
        }
    }

    /// <summary>Reconciles in place: rows that vanished are removed, new ones inserted at their position, the rest
    /// updated and moved — never Clear(), so the view keeps its containers and focus, and a reference held across a
    /// reload (an in-flight name edit) stays live.</summary>
    private void SyncFriends(List<FriendGroup> fresh)
    {
        for (var i = Friends.Count - 1; i >= 0; i--)
            if (fresh.All(f => f.Id != Friends[i].Model.Id)) Friends.RemoveAt(i);
        for (var i = 0; i < fresh.Count; i++)
        {
            var f = fresh[i];
            var at = -1;
            for (var j = 0; j < Friends.Count; j++) if (Friends[j].Model.Id == f.Id) { at = j; break; }
            if (at < 0) Friends.Insert(i, new FriendItemViewModel(f, this));
            else
            {
                Friends[at].ApplyModel(f);
                if (at != i) Friends.Move(at, i);
            }
        }
        for (var i = 0; i < Friends.Count; i++) Friends[i].Index = i;
    }
}

public sealed partial class FriendItemViewModel : ObservableObject
{
    private readonly FriendsViewModel _owner;
    private bool _loading = true;

    public FriendItemViewModel(FriendGroup model, FriendsViewModel owner)
    {
        Model = model;
        _owner = owner;
        _memberNames = model.MemberNames ?? "";
        _enabled = model.Enabled;
        _colorIndex = FriendPalette.IndexOf(model.ColorHex);
        _colorOptions = Array.Empty<ColorOption>();
        _loading = false;
    }

    public FriendGroup Model { get; private set; }
    public string GroupName => Model.GroupName;

    /// <summary>Position in the list; drives the appear cascade. Set by FriendsViewModel.SyncFriends.</summary>
    [ObservableProperty] private int _index;

    [ObservableProperty] private string _memberNames;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _colorIndex;
    [ObservableProperty] private IReadOnlyList<ColorOption> _colorOptions;

    /// <summary>Re-synced from a fresh DB read (SyncFriends): updates the bound display without
    /// re-triggering a save (the value already came from the database, not from the user).</summary>
    public FriendItemViewModel ApplyModel(FriendGroup fresh)
    {
        Model = fresh;
        OnPropertyChanged(nameof(GroupName));
        MemberNames = fresh.MemberNames ?? "";
        _loading = true;
        Enabled = fresh.Enabled;
        _loading = false;
        ColorIndex = FriendPalette.IndexOf(fresh.ColorHex);
        return this;
    }

    partial void OnEnabledChanged(bool value)
    {
        if (!_loading) _ = _owner.SaveAsync(this);
    }

    /// <summary>The names box saves on focus loss / Enter (view code-behind), not on every keystroke.</summary>
    [RelayCommand]
    private Task CommitNames() => MemberNames == (Model.MemberNames ?? "") ? Task.CompletedTask : _owner.SaveAsync(this);

    [RelayCommand] private Task PickColor(ColorOption option) => _owner.SetColorAsync(this, option.Index);
    [RelayCommand] private Task Remove() => _owner.RemoveAsync(this);
}
