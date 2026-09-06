using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Features.States;
using Vograph.Desktop.Services;
using Vograph.Desktop.ViewModels;
using MapInfo = Vograph.Core.Services.MapInfo;

namespace Vograph.Desktop.Shell;

/// <summary>Owns navigation, the sidebar state and the group card. Section view models are created lazily and cached.</summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly Dictionary<SectionKey, Func<ViewModelBase>> _factories = new();
    private readonly Dictionary<SectionKey, ViewModelBase> _sections = new();

    public ShellViewModel(AppServices app) : base(app)
    {
        NavigateCommand = new RelayCommand<string>(key =>
        {
            if (Enum.TryParse<SectionKey>(key, ignoreCase: true, out var k)) NavigateTo(k);
        });

        MainSections = new ObservableCollection<NavSection>
        {
            Make(SectionKey.Schedule, "navSchedule", "Icon.Calendar"),
            Make(SectionKey.Week, "navWeek", "Icon.Week"),
            Make(SectionKey.Summary, "navSummary", "Icon.Summary"),
        };
        ToolSections = new ObservableCollection<NavSection>
        {
            Make(SectionKey.Teachers, "navTeachers", "Icon.Teachers"),
            Make(SectionKey.Maps, "navMaps", "Icon.Map"),
            Make(SectionKey.Friends, "navFriends", "Icon.Friends"),
            Make(SectionKey.Homework, "navHomework", "Icon.Homework"),
        };
        SettingsSection = Make(SectionKey.Settings, "navSettings", "Icon.Settings");

        foreach (var key in Enum.GetValues<SectionKey>())
        {
            var k = key;
            Register(k, () => new PlaceholderViewModel(App, AllSections.First(s => s.Key == k).LabelKey));
        }
        Register(SectionKey.Schedule, () => new Features.Schedule.ScheduleViewModel(App, this));
        Register(SectionKey.Week, () => new Features.Week.WeekViewModel(App, this));

        SidebarCollapsed = app.Prefs.SidebarCollapsed;
        app.Loc.LanguageChanged += () =>
        {
            foreach (var s in AllSections) s.RefreshLabel();
            _ = RefreshGroupCardAsync();
        };
        // Startup only: the shell is built before any background Core call exists, so this is the one
        // synchronous read (same class as the AppServices ctor). Every later refresh goes through RefreshGroupCardAsync.
        ApplyGroupCard(ReadCard());
        // The schedule section is built by StartAsync against loaded data; until then the host shows the loading state.
        Current = new LoadingViewModel(App);
        CurrentKey = SectionKey.Schedule;
        MainSections[0].IsActive = true;
    }

    public ObservableCollection<NavSection> MainSections { get; }
    public ObservableCollection<NavSection> ToolSections { get; }
    public NavSection SettingsSection { get; }
    public IEnumerable<NavSection> AllSections => MainSections.Concat(ToolSections).Append(SettingsSection);
    public IRelayCommand<string> NavigateCommand { get; }
    public ToastService Toasts => App.Toasts;
    public DialogHostViewModel Dialogs { get; } = new();

    [ObservableProperty] private ViewModelBase? _current;
    [ObservableProperty] private SectionKey _currentKey;
    [ObservableProperty] private bool _sidebarCollapsed;
    [ObservableProperty] private string _groupName = "—";
    [ObservableProperty] private string _groupSubtitle = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStale))]
    private string? _staleText;

    [ObservableProperty] private bool _staleWarn;

    public bool HasStale => StaleText is not null;

    /// <summary>Map the Maps section should show when it opens (set by the ◉ action on a lesson).</summary>
    public MapInfo? PendingMap { get; private set; }

    /// <summary>Raised after the user picks another group; sections reload themselves.</summary>
    public event Action? GroupChanged;

    /// <summary>Raised after the timetable cache changed (refresh, import): sections recompose.</summary>
    public event Action? ScheduleChanged;
    internal void RaiseScheduleChanged() => ScheduleChanged?.Invoke();

    [ObservableProperty] private bool _isRefreshing;
    private bool _staleToastShown;
    private DispatcherTimer? _autoCheck;

    /// <summary>F5 and «Обновить расписание».</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task RefreshSchedule() => RefreshScheduleAsync(force: true, quiet: false);

    /// <summary>Network outside the gate (Refresher), parse + SQLite inside (Parser.RefreshAsync(xmlOverride)).
    /// quiet: startup / 24 h check — only the first failure per session toasts.</summary>
    public async Task<bool> RefreshScheduleAsync(bool force, bool quiet)
    {
        if (IsRefreshing) return false;
        IsRefreshing = true;
        try
        {
            var settings = await RunAsync(() => App.Db.GetSettings(), "settings");
            if (settings is null) return false;
            RefreshCheck check;
            try
            {
                check = await App.Refresher.CheckAsync(force ? null : settings.LastFetchedAt);
            }
            catch (Exception ex)
            {
                App.Log.Error("refresh", ex);
                if (!quiet || !_staleToastShown) App.Toasts.Warn(T("refreshFail", ex.Message));
                _staleToastShown = true;
                return false;
            }
            if (check.Modified)
            {
                var xml = check.Xml!;
                // Block-bodied async lambda: Parser.RefreshAsync returns Task<ValueTuple>, which would bind to the
                // Func<T> overload (T = the Task itself) and leave the SQLite write running past the gate release.
                if (!await RunAsync(async () => { await App.Parser.RefreshAsync(xmlOverride: xml); }, "refresh")) return false;
                await RefreshGroupCardAsync();
                RaiseScheduleChanged();
                if (!quiet) App.Toasts.Ok(T("refreshOk"));
                return true;
            }
            await RunAsync(() =>
            {
                var s = App.Db.GetSettings();
                s.LastAutoCheckAt = DateTime.UtcNow.ToString("o");
                App.Db.SaveSettings(s);
            }, "settings");
            await RefreshGroupCardAsync();
            if (!quiet) App.Toasts.Info(T("refreshNone"));
            return false;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>The 24 h rule of the old AutoRefreshService: check when the last check (or fetch) is a day old.</summary>
    public static bool ShouldAutoCheck(Settings s, DateTime utcNow)
    {
        var last = s.LastAutoCheckAt ?? s.LastFetchedAt;
        if (!DateTime.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)) return true;
        return (utcNow - at.ToUniversalTime()).TotalHours >= 24;
    }

    private void StartAutoCheck()
    {
        if (_autoCheck is not null) return;
        _autoCheck = new DispatcherTimer(TimeSpan.FromHours(1), DispatcherPriority.Background, async (_, _) =>
        {
            var s = await RunAsync(() => App.Db.GetSettings(), "settings");
            if (s is not null && ShouldAutoCheck(s, DateTime.UtcNow)) await RefreshScheduleAsync(force: false, quiet: true);
        });
        _autoCheck.Start();
    }

    /// <summary>Week/Teachers: jump to a concrete date in the schedule section.</summary>
    public void OpenScheduleAt(DateTime date)
    {
        NavigateTo(SectionKey.Schedule);
        if (Current is ScheduleViewModel s) s.ShowDate(date);
    }

    /// <summary>Bare keys that must not fire inside text fields or over a dialog; MainWindow calls this from its bubbling KeyDown handler.</summary>
    public bool HandleShortcut(Key key)
    {
        if (Dialogs.HasDialog) return false;
        if (CurrentKey != SectionKey.Schedule || Current is not ScheduleViewModel s) return false;
        switch (key)
        {
            case Key.Left: s.PrevDayCommand.Execute(null); return true;
            case Key.Right: s.NextDayCommand.Execute(null); return true;
            case Key.Home: s.GoTodayCommand.Execute(null); return true;
            default: return false;
        }
    }

    private NavSection Make(SectionKey key, string labelKey, string iconKey) => new(key, labelKey, iconKey, NavigateCommand);

    /// <summary>Later tasks replace the placeholder factory of a section with the real one.</summary>
    public void Register(SectionKey key, Func<ViewModelBase> factory)
    {
        _factories[key] = factory;
        DetachSection(key);
    }

    private void DetachSection(SectionKey key)
    {
        if (_sections.Remove(key, out var vm)) vm.Detach();
    }

    public T Section<T>(SectionKey key) where T : ViewModelBase => (T)GetOrCreate(key);

    private ViewModelBase GetOrCreate(SectionKey key)
    {
        if (!_sections.TryGetValue(key, out var vm))
        {
            vm = _factories[key]();
            _sections[key] = vm;
        }
        return vm;
    }

    public void NavigateTo(SectionKey key)
    {
        Current = GetOrCreate(key);
        CurrentKey = key;
        foreach (var s in AllSections) s.IsActive = s.Key == key;
        _ = Current.ActivateAsync(); // implementations run under RunAsync and never throw
    }

    public void ShowMap(MapInfo? info)
    {
        PendingMap = info;
        NavigateTo(SectionKey.Maps);
    }

    [RelayCommand]
    private void ToggleSidebar() => SidebarCollapsed = !SidebarCollapsed;

    partial void OnSidebarCollapsedChanged(bool value)
    {
        foreach (var s in AllSections) s.IsCompact = value;
        App.Prefs.SidebarCollapsed = value;
        App.Prefs.Save();
    }

    [RelayCommand]
    private void ToggleTheme() => App.Theme?.Toggle();

    private sealed record StartData(int GroupCount, Settings Settings);

    /// <summary>Cache-first startup (spec §8): with data in SQLite the schedule composes at once and the network
    /// runs behind it; the loading state and a gated bootstrap remain only for an empty database.</summary>
    public async Task StartAsync(bool allowNetwork = true)
    {
        var data = await RunAsync(() => new StartData(App.Db.GetAllGroups().Count, App.Db.GetSettings()), "startup");
        if (data is null)
        {
            Current = new ErrorStateViewModel(App, null, () => StartAsync(allowNetwork));
            return;
        }
        if (data.GroupCount == 0)
        {
            Current = new LoadingViewModel(App);
            var result = await RunAsync(() => DataBootstrap.RunAsync(App, allowNetwork), "bootstrap");
            if (result is null || !result.HasData)
            {
                Current = new ErrorStateViewModel(App, result?.Error, () => StartAsync(allowNetwork));
                return;
            }
            if (result.Stale && result.Error is not null) App.Toasts.Warn($"{T("stale").Trim(' ', '·')}: {result.Error}");
        }
        try
        {
            await RunAsync(() => App.Homework.RecomputeAllStatuses(), "homework statuses");
            await RefreshGroupCardAsync();
            DetachSection(SectionKey.Schedule); // rebuild against fresh data
            NavigateTo(SectionKey.Schedule);
            await Section<ScheduleViewModel>(SectionKey.Schedule).InitializeAsync();
            if (allowNetwork)
            {
                if (data.GroupCount > 0 && ShouldAutoCheck(data.Settings, DateTime.UtcNow)) _ = RefreshScheduleAsync(force: false, quiet: true);
                StartAutoCheck();
            }
        }
        catch (Exception ex)
        {
            App.Log.Error("startup", ex);
            Current = new ErrorStateViewModel(App, ex.Message, () => StartAsync(allowNetwork));
        }
    }

    private sealed record PickerData(List<Group> Groups, string? CurrentId);

    [RelayCommand]
    private async Task OpenGroupPickerAsync()
    {
        var data = await RunAsync(() => new PickerData(App.Db.GetAllGroups(), App.Db.GetSettings().MyGroupId), "groups");
        if (data is null) return;
        var dlg = new GroupPickerDialogViewModel(data.Groups, data.CurrentId);
        if (!await Dialogs.ShowAsync(dlg) || dlg.Selected is null) return;
        var chosen = dlg.Selected;
        var saved = await RunAsync(() =>
        {
            var s = App.Db.GetSettings();
            s.MyGroupId = chosen.Id;
            App.Db.SaveSettings(s);
            App.Homework.RecomputeAllStatuses();
        }, "group");
        if (!saved) return;
        await RefreshGroupCardAsync(); // before RaiseGroupChanged: sections read GroupName while they react
        RaiseGroupChanged();
        App.Toasts.Ok(T("savedOk"));
    }

    private sealed record CardData(Settings Settings, Group? Group);

    private CardData ReadCard()
    {
        var s = App.Db.GetSettings();
        return new CardData(s, string.IsNullOrEmpty(s.MyGroupId) ? null : App.Db.GetGroup(s.MyGroupId));
    }

    /// <summary>Re-reads settings + group under the Core gate, then updates the card.</summary>
    public async Task RefreshGroupCardAsync()
    {
        var data = await RunAsync(ReadCard, "group card");
        if (data is not null) ApplyGroupCard(data);
    }

    private void ApplyGroupCard(CardData data)
    {
        var settings = data.Settings;
        var group = data.Group;
        if (group is null)
        {
            GroupName = T("noGroup");
            GroupSubtitle = T("noGroupHint");
            return;
        }
        var periodStart = DateTime.TryParse(settings.PeriodStart, out var ps) ? ps : new DateTime(DateTime.Today.Year, 9, 1);
        var weekCount = settings.WeekCount > 0 ? settings.WeekCount : 2;
        var isOdd = ParityService.IsOddWeek(DateTime.Today, periodStart, weekCount, settings.ParityInvert);
        var culture = CultureInfo.GetCultureInfo(App.Loc.Language == "en" ? "en-US" : "ru-RU");
        GroupName = group.Name;
        GroupSubtitle = $"{T("parityWeek", App.I18n.FormatParity(isOdd))} · {DateTime.Today.ToString(App.Loc.Language == "en" ? "MMM d" : "d MMM", culture)}";
        var (stale, warn) = GroupCardLogic.Stale(settings.LastFetchedAt, DateTime.UtcNow, App.Loc);
        StaleText = stale;
        StaleWarn = warn;
    }

    /// <summary>Sealed type: 'internal' rather than 'protected' so later dialogs in this assembly can raise it without CS0628.</summary>
    internal void RaiseGroupChanged() => GroupChanged?.Invoke();
}
