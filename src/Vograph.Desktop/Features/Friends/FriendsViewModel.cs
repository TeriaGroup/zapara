using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Models;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Friends;

public sealed record ColorOption(int Index, bool IsCurrent);

public sealed partial class FriendsViewModel : ViewModelBase
{
    private const int MaxFriends = 5;
    private readonly ShellViewModel _shell;
    private readonly Func<DateTime> _clock;
    private readonly Action _reload;
    private bool _suppress;
    private int _version;
    private Task? _pendingSettingsSave;

    public FriendsViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _tickLabels = BuildTicks();
        _strictnessLabel = LabelFor(25);
        _reload = () => _ = LoadAsync();
        shell.GroupChanged += _reload;
        shell.ScheduleChanged += _reload;
        app.Loc.LanguageChanged += _reload;
    }

    public override void Detach()
    {
        _shell.GroupChanged -= _reload;
        _shell.ScheduleChanged -= _reload;
        App.Loc.LanguageChanged -= _reload;
    }

    public override Task ActivateAsync() => LoadAsync();

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

    // Short tick labels for the slider (strictTick25..100) are their own keys, distinct from the long
    // inter25..100 texts the schedule's dot tooltips use ("в том же корпусе" etc. would not fit under a tick).
    private IList<string> BuildTicks() => new[] { T("strictTick25"), T("strictTick50"), T("strictTick75"), T("strictTick100") };
    private string LabelFor(double v) => T(v >= 100 ? "strictTick100" : v >= 75 ? "strictTick75" : v >= 50 ? "strictTick50" : "strictTick25");

    partial void OnStrictnessChanged(double value)
    {
        StrictnessLabel = LabelFor(value);
        if (!_suppress) _pendingSettingsSave = SaveSettingsAsync();
    }

    partial void OnAlwaysShowAllChanged(bool value)
    {
        if (!_suppress) _pendingSettingsSave = SaveSettingsAsync();
    }

    private sealed record PreviewData(string Line, IReadOnlyList<FriendMark> Marks);
    private sealed record FriendsData(List<FriendGroup> Friends, Settings Settings, PreviewData? Preview);

    public async Task LoadAsync()
    {
        var version = ++_version;
        var today = _clock().Date;
        var data = await RunAsync(() =>
        {
            var friends = App.Db.GetFriends();
            var settings = App.Db.GetSettings();
            return new FriendsData(friends, settings, ComputePreview(friends, settings, today));
        }, "friends");
        if (data is null || version != _version) return;
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

    /// <summary>The nearest lesson (≤ 14 days) where at least one friend is around — otherwise the first lesson with dots when «always show» is on.</summary>
    private PreviewData? ComputePreview(List<FriendGroup> friends, Settings settings, DateTime today)
    {
        if (string.IsNullOrEmpty(settings.MyGroupId) || friends.Count == 0) return null;
        var loc = App.Loc;
        PreviewData? fallback = null;
        for (var i = 0; i < 14; i++)
        {
            var date = today.AddDays(i);
            foreach (var l in App.Schedule.GetSchedule(date, settings.MyGroupId).OrderBy(x => TimeSpan.TryParse(x.TimeStart, out var t) ? t : TimeSpan.Zero))
            {
                var marks = FriendMarks.Compute(App, l, date, friends, settings, loc);
                if (marks.Count == 0) continue;
                var name = ScheduleComposer.StripType(App.Overrides.GetDisplayName(l.SubjectRaw, l.DayOfWeek), l.TypeRaw);
                var line = $"{loc.I18n.FormatDay(date)} {DayTitles.ShortDate(date, loc)} · {l.TimeStart} · {name}";
                var data = new PreviewData(line, marks);
                if (marks.Any(m => m.Fill != Controls.DotFill.Off)) return data;
                fallback ??= data;
            }
        }
        return fallback;
    }

    private void ApplyPreview(PreviewData? p)
    {
        HasPreview = p is not null;
        PreviewLine = p?.Line ?? T("previewNone");
        PreviewMarks = p is null ? Array.Empty<FriendMarkViewModel>() : p.Marks.Select(m => new FriendMarkViewModel(m)).ToList();
    }

    /// <summary>Called explicitly after Strictness/AlwaysShowAll change (and by Save/SetColor). Awaits any
    /// settings write still in flight first — that write's own RaiseScheduleChanged loops back into this
    /// view model's _reload, which would otherwise race this method's _version guard and drop its result.</summary>
    public async Task RefreshPreviewAsync()
    {
        if (_pendingSettingsSave is { } pending) await pending;
        var version = ++_version;
        var today = _clock().Date;
        var preview = await RunAsync(() => ComputePreview(App.Db.GetFriends(), App.Db.GetSettings(), today) ?? new PreviewData("", Array.Empty<FriendMark>()), "friends");
        if (preview is null || version != _version) return;
        ApplyPreview(preview.Line.Length == 0 ? null : preview);
    }

    private async Task SaveSettingsAsync()
    {
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
        _shell.RaiseScheduleChanged();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Add()
    {
        if (!CanAdd) return;
        var taken = Friends.Select(f => f.GroupName).ToHashSet();
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
        await LoadAsync();
        _shell.RaiseScheduleChanged();
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
        var confirm = new ConfirmDialogViewModel(T("friendsRemove"), T("friendsRemoveConfirm", item.GroupName), T("delete"), danger: true);
        if (!await _shell.Dialogs.ShowAsync(confirm)) return;
        if (!await RunAsync(() => App.Db.DeleteFriend(item.Model.Id), "friend delete")) return;
        await LoadAsync();
        _shell.RaiseScheduleChanged();
    }

    /// <summary>Names / enabled flag: persist the item's model as it is now.</summary>
    public async Task SaveAsync(FriendItemViewModel item)
    {
        item.Model.MemberNames = item.MemberNames;
        item.Model.Enabled = item.Enabled;
        if (!await RunAsync(() => App.Db.UpdateFriend(item.Model), "friend save")) return;
        _shell.RaiseScheduleChanged();
        await RefreshPreviewAsync();
    }

    public async Task SetColorAsync(FriendItemViewModel item, int index)
    {
        if (index < 0 || index >= FriendPalette.Hex.Length) return;
        if (Friends.Any(f => !ReferenceEquals(f, item) && f.ColorIndex == index)) return; // taken
        item.Model.ColorHex = FriendPalette.Hex[index];
        if (!await RunAsync(() => App.Db.UpdateFriend(item.Model), "friend color")) return;
        item.ColorIndex = index;
        RefreshColorOptions();
        _shell.RaiseScheduleChanged();
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

    /// <summary>Reuses existing item view models by Model.Id instead of rebuilding the collection from scratch,
    /// so a reference held across a reload (e.g. by the view, or by an in-flight color/name edit) keeps
    /// reflecting live state instead of being silently orphaned.</summary>
    private void SyncFriends(List<FriendGroup> friends)
    {
        var existing = Friends.ToDictionary(f => f.Model.Id);
        Friends.Clear();
        foreach (var f in friends)
            Friends.Add(existing.TryGetValue(f.Id, out var item) ? item.ApplyModel(f) : new FriendItemViewModel(f, this));
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
