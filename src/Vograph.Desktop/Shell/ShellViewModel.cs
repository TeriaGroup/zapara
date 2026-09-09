using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Models;
using Vograph.Core.Services.Sync;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Features.States;
using Vograph.Desktop.Services;
using Vograph.Desktop.ViewModels;
using Zapara.Contracts.Sync;
using MapInfo = Vograph.Core.Services.MapInfo;

namespace Vograph.Desktop.Shell;

/// <summary>Owns navigation, the sidebar state and the group card. Section view models are created lazily and cached.</summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly Dictionary<SectionKey, Func<ViewModelBase>> _factories = new();
    private readonly Dictionary<SectionKey, ViewModelBase> _sections = new();

    public ShellViewModel(AppServices app, bool attach = true) : base(app)
    {
        // One update state for the whole window: the sidebar item, the Settings card and the startup flow share it.
        Updates = new UpdateCheckViewModel(app, () => Clock());
        Dialogs = new DialogHostViewModel(app.Motion, app.Work);

        NavigateCommand = new RelayCommand<string>(key =>
        {
            if (Enum.TryParse<SectionKey>(key, ignoreCase: true, out var k)) NavigateTo(k);
        });

        MainSections = new ObservableCollection<NavSection>
        {
            Make(SectionKey.Schedule, "navSchedule", "Icon.Calendar", "Ctrl+1"),
            Make(SectionKey.Week, "navWeek", "Icon.Week", "Ctrl+2"),
            Make(SectionKey.Summary, "navSummary", "Icon.Summary", "Ctrl+3"),
        };
        ToolSections = new ObservableCollection<NavSection>
        {
            Make(SectionKey.Teachers, "navTeachers", "Icon.Teachers", "Ctrl+4"),
            Make(SectionKey.Maps, "navMaps", "Icon.Map", "Ctrl+5"),
            Make(SectionKey.Friends, "navFriends", "Icon.Friends", "Ctrl+6"),
            Make(SectionKey.Homework, "navHomework", "Icon.Homework", "Ctrl+7"),
            Make(SectionKey.Community, "navCommunity", "Icon.Community", "Ctrl+9"),
        };
        SettingsSection = Make(SectionKey.Settings, "navSettings", "Icon.Settings", "Ctrl+8");

        // Every SectionKey has a real section; a missing entry here is a KeyNotFoundException on navigation,
        // which is what SectionsRenderTests and ShellTests guard.
        Register(SectionKey.Schedule, () => new Features.Schedule.ScheduleViewModel(App, this));
        Register(SectionKey.Week, () => new Features.Week.WeekViewModel(App, this));
        Register(SectionKey.Summary, () => new Features.Summary.SummaryViewModel(App, this));
        Register(SectionKey.Teachers, () => new Features.Teachers.TeachersViewModel(App, this, allowNetwork: App.AllowNetwork));
        Register(SectionKey.Maps, () => new Features.Maps.MapsViewModel(App, this));
        Register(SectionKey.Friends, () => new Features.Friends.FriendsViewModel(App, this));
        Register(SectionKey.Homework, () => new Features.Homeworks.HomeworkViewModel(App, this));
        Register(SectionKey.Community, () => new Features.Communities.CommunitiesViewModel(App));
        Register(SectionKey.Settings, () => new Features.Preferences.SettingsViewModel(App, this));

        _sidebarCollapsed = app.Prefs.SidebarCollapsed;
        foreach (var section in AllSections) section.IsCompact = _sidebarCollapsed;
        if (app.Theme is { } theme)
        {
            IsDark = theme.IsDark;
        }
        // A phone pushing over the LAN must show up even when Settings was never opened this session, so the
        // shell — not a section — owns this subscription. The event is raised on a pool thread, and the
        // fire-and-forget hop to the UI thread is safe only because NotifyImportedAsync cannot throw: every
        // step of it is a RunAsync (gated, logged, toasted) or a plain event raise.
        if (attach) Attach();
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
    public DialogHostViewModel Dialogs { get; }

    /// <summary>Update check/download state, shown by the sidebar item and the Settings card.</summary>
    public UpdateCheckViewModel Updates { get; }

    /// <summary>Set by App: closes the application when the update batch has been started.</summary>
    public Action? Shutdown { get => Updates.Shutdown; set => Updates.Shutdown = value; }

    /// <summary>The sidebar «Обновление» item: confirm, then download and restart.</summary>
    [RelayCommand]
    private async Task OpenUpdateDialog()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        if (!Updates.IsAvailable || Updates.LatestTag is null) return;
        var dlg = new UpdateDialogViewModel(Updates.LatestTag, Updates.PublishedText);
        if (await Dialogs.ShowAsync(dlg)) await Updates.InstallAsync();
    }

    /// <summary>409 keep-local / keep-server, or 410 abort. Posted off the coordinator so CoreGate is released first.</summary>
    internal async Task ShowSyncConflictAsync(PrivateSyncConflict conflict)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        ArgumentNullException.ThrowIfNull(conflict);
        var dialog = IsExpiredConflict(conflict)
            ? SyncConflictDialogViewModel.ForExpired(conflict)
            : await BuildConflictDialogAsync(conflict);
        if (!operation.IsCurrent) return;
        await Dialogs.ShowAsync(dialog);
    }

    private void OnPrivateSyncConflict(PrivateSyncConflict conflict) =>
        App.Work.Post(a => Dispatcher.UIThread.Post(a), () => ShowSyncConflictAsync(conflict),
            ex => App.Log.Error("sync conflict dialog", ex));

    private static bool IsExpiredConflict(PrivateSyncConflict conflict) =>
        conflict.Diagnostic.Contains("устарел", StringComparison.Ordinal);

    private async Task<SyncConflictDialogViewModel> BuildConflictDialogAsync(PrivateSyncConflict conflict)
    {
        var box = await RunAsync(() => new ConflictBox(TryReadConflictPayload(conflict)), "sync conflict");
        return box?.Payload is { Server: { } server } payload
            ? SyncConflictDialogViewModel.ForConflict(conflict, payload.Local, server, payload.NewOpId)
            : SyncConflictDialogViewModel.ForExpired(conflict);
    }

    private sealed record ConflictBox(ConflictPayload? Payload);
    private sealed record ConflictPayload(SyncValue? Local, SyncRecord Server, Guid NewOpId);

    private ConflictPayload? TryReadConflictPayload(PrivateSyncConflict conflict)
    {
        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText = "SELECT localPayload, serverPayload FROM sync_draft WHERE entityType=@t AND entityId=@id";
        cmd.Parameters.AddWithValue("@t", conflict.EntityType);
        cmd.Parameters.AddWithValue("@id", conflict.EntityId.ToString("D"));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var localJson = reader.IsDBNull(0) ? null : reader.GetString(0);
        var serverJson = reader.IsDBNull(1) ? null : reader.GetString(1);
        if (string.IsNullOrWhiteSpace(serverJson) || serverJson == "{}") return null;
        try
        {
            var server = SyncJson.Parse<SyncRecord>(Encoding.UTF8.GetBytes(serverJson));
            SyncValue? local = null;
            if (!string.IsNullOrWhiteSpace(localJson) && localJson != "{}")
            {
                var bytes = Encoding.UTF8.GetBytes(localJson);
                local = conflict.EntityType switch
                {
                    "homework" => SyncJson.Parse<HomeworkValue>(bytes),
                    "completion" => SyncJson.Parse<CompletionValue>(bytes),
                    "override" => SyncJson.Parse<OverrideValue>(bytes),
                    "friend" => SyncJson.Parse<FriendValue>(bytes),
                    "settings" => SyncJson.Parse<SettingsValue>(bytes),
                    _ => null
                };
            }
            return new ConflictPayload(local, server, Guid.NewGuid());
        }
        catch (ArgumentException) { return null; }
    }

    [ObservableProperty] private ViewModelBase? _current;
    [ObservableProperty] private SectionKey _currentKey;
    [ObservableProperty] private bool _sidebarCollapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupCardTip))]
    private string _groupName = "—";

    [ObservableProperty] private string _groupSubtitle = "";

    [ObservableProperty] private string _groupRailLabel = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStale), nameof(ShowStaleChip), nameof(ShowStaleDot), nameof(GroupCardTip))]
    private string? _staleText;

    [ObservableProperty] private bool _staleWarn;

    public bool HasStale => StaleText is not null;
    public bool ShowStaleChip => HasStale && !SidebarCollapsed;
    public bool ShowStaleDot => HasStale && SidebarCollapsed;

    /// <summary>Expanded: «Моя группа». On the rail the tooltip carries what the card cannot show: the number and the stale chip's text.</summary>
    public string GroupCardTip => SidebarCollapsed ? (StaleText is null ? GroupName : $"{GroupName}\n{StaleText}") : T("myGroup");

    public string SidebarToggleTip => T(SidebarCollapsed ? "sidebarExpandTip" : "sidebarToggleTip");

    /// <summary>Mirrors ThemeService.IsDark for the footer button's glyph (Sun in the dark, Moon in the light).</summary>
    [ObservableProperty] private bool _isDark;

    /// <summary>Set by MainWindow from its WindowState; drives the maximize button's glyph and tooltip.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaximizeTip))]
    private bool _isMaximized;

    public string MaximizeTip => T(IsMaximized ? "winRestore" : "winMaximize");

    /// <summary>Full-window content above every section (the fullscreen map); Escape closes it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlay))]
    private ViewModelBase? _overlay;

    public bool HasOverlay => Overlay is not null;

    /// <summary>Map the Maps section should show when it opens (set by the ◉ action on a lesson).</summary>
    public MapInfo? PendingMap { get; private set; }

    /// <summary>Display name of the lesson the pending map came from, so the Maps header can read
    /// «Пара: Матан · 493 · …» instead of repeating the room. Null when no name is known.</summary>
    public string? PendingLessonName { get; private set; }

    /// <summary>Read once by the Maps section when it activates: a handover, not a standing selection.</summary>
    internal (MapInfo? Map, string? LessonName) TakePendingMap()
    {
        var handover = (PendingMap, PendingLessonName);
        PendingMap = null;
        PendingLessonName = null;
        return handover;
    }

    /// <summary>Raised after the user picks another group; sections reload themselves.</summary>
    public event Action? GroupChanged;

    /// <summary>Raised after the timetable cache changed (refresh, import): sections recompose.</summary>
    public event Action? ScheduleChanged;

    /// <summary>Pure event invocation: callers that need the badge refreshed await
    /// UpdateHomeworkBadgeAsync themselves, after raising (see RefreshScheduleAsync).</summary>
    internal void RaiseScheduleChanged() { if (CanPublish) ScheduleChanged?.Invoke(); }

    /// <summary>Injected clock for badge/status computations (tests pin a date).</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    /// <summary>Raised after homework changed anywhere (schedule card or Homework section).</summary>
    public event Action? HomeworkChanged;

    /// <summary>Pure event invocation: every caller awaits UpdateHomeworkBadgeAsync itself right after
    /// raising (ScheduleViewModel's RaiseHomeworkAsync, HomeworkViewModel.ChangedAsync). A fire-and-forget
    /// call here used to keep running after an awaited caller returned, racing test teardown's Dispose of
    /// the SQLite connection it reads from.</summary>
    internal void RaiseHomeworkChanged() { if (CanPublish) HomeworkChanged?.Invoke(); }

    /// <summary>Data arrived from outside the app — a LAN push or the Settings file import. Both land in SQLite
    /// through Core's SyncService, which leaves the homework due dates computed against the *sender's* timetable,
    /// so they are recomputed here before every section recomposes and the sidebar badge is refreshed. The group
    /// card is refreshed too (R50): an import adopts MyGroupId when the receiver had none and overwrites
    /// ParityInvert when the payload is newer, and the card shows both.
    /// Never throws (RunAsync swallows and reports), which is what makes the ctor's fire-and-forget hop safe.</summary>
    public async Task NotifyImportedAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        if (App.Api.Configured) { App.Api.Invalidate(); await EnsureApiNeedsAsync(); }
        await RunAsync(() => App.Homework.RecomputeAllStatuses(), "import");
        await RefreshGroupCardAsync();
        RaiseScheduleChanged();
        RaiseHomeworkChanged();
        await UpdateHomeworkBadgeAsync();
    }

    private sealed record BadgeData(int Count);

    public async Task UpdateHomeworkBadgeAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var today = Clock().Date;
        var data = await RunAsync(() => new BadgeData(HomeworkStatus.BadgeCount(App.Homework.GetAll(), today)), "homework badge");
        if (data is null || !operation.IsCurrent) return;
        var section = ToolSections.FirstOrDefault(s => s.Key == SectionKey.Homework);
        if (section is not null) section.Badge = data.Count > 0 ? data.Count.ToString() : null;
    }

    [ObservableProperty] private bool _isRefreshing;
    private bool _staleToastShown;
    private bool _started;
    private bool _stopped;
    private bool StartupStopped => _stopped || !CanPublish || (App.Api.Configured && App.Api.LifetimeToken.IsCancellationRequested);
    private DispatcherTimer? _autoCheck;

    /// <summary>F5 and «Обновить расписание».</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task RefreshSchedule() => RefreshScheduleAsync(force: true, quiet: false);

    /// <summary>Network outside the gate (Refresher), parse + SQLite inside (Parser.RefreshAsync(xmlOverride)).
    /// quiet: startup / 24 h check — only the first failure per session toasts.</summary>
    public async Task<bool> RefreshScheduleAsync(bool force, bool quiet)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return false;
        if (App.Api.Configured) return await RefreshApiScheduleAsync(quiet);
        // VOGRAPH_OFFLINE=1 promises «no timetable refresh» (App.axaml.cs), and AppServices.AllowNetwork only
        // ever gated the automatic paths: F5 and Settings' «Обновить расписание» went straight out to the
        // network on an offline run, contradicting the switch's own comment (T12-R6). The wording is the
        // existing refresh failure, with the reason in place of the exception message.
        if (_started && !App.AllowNetwork)
        {
            App.Log.Info("refresh: skipped, network disabled for this run");
            if (!quiet) App.Toasts.Warn(T("refreshFail", T("offlineMode")));
            return false;
        }
        if (IsRefreshing) return false;
        IsRefreshing = true;
        try
        {
            var settings = await RunAsync(() => App.Db.GetSettings(), "settings");
            if (settings is null) return false;
            RefreshCheck check;
            try
            {
                check = await App.Refresher.CheckAsync(force ? null : settings.LastFetchedAt, operation.Token);
            }
            catch (Exception ex)
            {
                App.Log.Error("refresh", ex);
                if (!operation.IsCurrent) return false;
                if (!quiet || !_staleToastShown) App.Toasts.Warn(T("refreshFail", ex.Message));
                _staleToastShown = true;
                return false;
            }
            if (!operation.IsCurrent) return false;
            if (check.Modified)
            {
                var xml = check.Xml!;
                // Block-bodied async lambda: Parser.RefreshAsync returns Task<ValueTuple>, which would bind to the
                // Func<T> overload (T = the Task itself) and leave the SQLite write running past the gate release.
                // The successful fetch is also the last check: without the stamp the hourly tick could issue one
                // more HEAD within the same day (T1 #4).
                if (!await RunAsync(async () =>
                {
                    await App.Parser.RefreshAsync(xmlOverride: xml);
                    var s = App.Db.GetSettings();
                    s.LastAutoCheckAt = DateTime.UtcNow.ToString("o");
                    App.Db.SaveSettings(s);
                }, "refresh")) return false;
                await RefreshGroupCardAsync();
                RaiseScheduleChanged();
                await UpdateHomeworkBadgeAsync(); // due dates are recomputed against the new timetable
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

    internal void StartAutoCheck()
    {
        if (StartupStopped || _autoCheck is not null) return;
        _autoCheck = new DispatcherTimer(TimeSpan.FromHours(1), DispatcherPriority.Background, async (_, _) =>
        {
            using var operation = App.Work.Enter();
            if (!operation.IsCurrent) return;
            var s = await RunAsync(() => App.Db.GetSettings(), "settings");
            if (s is not null && ShouldAutoCheck(s, DateTime.UtcNow)) await RefreshScheduleAsync(force: false, quiet: true);
        });
        _autoCheck.Start();
    }

    internal bool IsAutoCheckRunning => _autoCheck is { IsEnabled: true };

    /// <summary>Shutdown: stops the hourly check and detaches every cached section (their Loc/shell subscriptions
    /// die with them). App calls this from desktop.Exit before AppServices.Dispose; tests call it directly.</summary>
    public void Stop()
    {
        _stopped = true;
        DetachSubscriptions();
        Dialogs.DismissCommand.Execute(null);
        App.Api.Stop();
        _autoCheck?.Stop();
        _autoCheck = null;
        foreach (var key in _sections.Keys.ToList()) DetachSection(key);
    }

    /// <summary>Week/Teachers: jump to a concrete date in the schedule section.</summary>
    public void OpenScheduleAt(DateTime date)
    {
        NavigateTo(SectionKey.Schedule);
        if (Current is ScheduleViewModel s) s.ShowDate(date);
    }

    /// <summary>Bare keys that must not fire inside text fields or over a dialog; MainWindow calls this from its bubbling KeyDown handler.
    /// Escape is the exception that does fire over them: it closes the topmost layer — the dialog first (it is drawn
    /// above the overlay), then the fullscreen map — and is therefore also let through from a focused text field.</summary>
    public bool HandleShortcut(Key key)
    {
        if (!CanPublish) return false;
        if (key == Key.Escape)
        {
            if (Dialogs.HasDialog) { Dialogs.DismissCommand.Execute(null); return true; }
            if (Overlay is not null) { Overlay = null; return true; }
            return false;
        }
        if (Dialogs.HasDialog || Overlay is not null) return false;
        if (CurrentKey != SectionKey.Schedule || Current is not ScheduleViewModel s) return false;
        switch (key)
        {
            case Key.Left: s.PrevDayCommand.Execute(null); return true;
            case Key.Right: s.NextDayCommand.Execute(null); return true;
            case Key.Home: s.GoTodayCommand.Execute(null); return true;
            default: return false;
        }
    }

    private NavSection Make(SectionKey key, string labelKey, string iconKey, string hotkey) => new(key, labelKey, iconKey, hotkey, NavigateCommand);

    /// <summary>Replaces a section's factory (tests pin the clock this way); the cached instance is detached so the
    /// next navigation rebuilds the section from the new factory. When that instance is the one on screen, the host
    /// is re-navigated at once: a section that no longer listens must not stay visible (T1 #8).</summary>
    public void Register(SectionKey key, Func<ViewModelBase> factory)
    {
        _factories[key] = factory;
        var wasCurrent = _sections.TryGetValue(key, out var old) && ReferenceEquals(Current, old);
        DetachSection(key);
        if (wasCurrent) NavigateTo(key);
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
        if (StartupStopped) return;
        // Ctrl+1…9 are Window.KeyBindings and fire straight into NavigateCommand, over the fullscreen map too
        // (HandleShortcut never sees them). Without this the section would be switched invisibly behind the plan.
        Overlay = null;
        Current = GetOrCreate(key);
        CurrentKey = key;
        foreach (var s in AllSections) s.IsActive = s.Key == key;
        _ = Current.ActivateAsync(); // implementations run under RunAsync and never throw
    }

    /// <summary>◉ on a lesson card: the caller passes the name the card shows, so the section can name the lesson.</summary>
    public void ShowMap(MapInfo? info, string? lessonName = null)
    {
        PendingMap = info;
        PendingLessonName = lessonName;
        NavigateTo(SectionKey.Maps);
    }

    [RelayCommand]
    private void ToggleSidebar() { if (CanPublish) SidebarCollapsed = !SidebarCollapsed; }

    partial void OnSidebarCollapsedChanged(bool value)
    {
        if (!CanPublish) return;
        foreach (var s in AllSections) s.IsCompact = value;
        App.Prefs.SidebarCollapsed = value;
        App.Prefs.Save();
        OnPropertyChanged(nameof(ShowStaleChip));
        OnPropertyChanged(nameof(ShowStaleDot));
        OnPropertyChanged(nameof(GroupCardTip));
        OnPropertyChanged(nameof(SidebarToggleTip));
    }

    [RelayCommand]
    private void ToggleTheme() { if (CanPublish) App.Theme?.Toggle(); }

    private sealed record StartData(int GroupCount, Settings Settings);

    /// <summary>Cache-first startup (spec §8): with data in SQLite the schedule composes at once and the network
    /// runs behind it; the loading state and a gated bootstrap remain only for an empty database.</summary>
    public async Task StartAsync(bool allowNetwork = true)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        try { await StartCoreAsync(allowNetwork); }
        catch (OperationCanceledException) when (!operation.IsCurrent || (App.Api.Configured && App.Api.LifetimeToken.IsCancellationRequested)) { return; }
    }

    private async Task StartCoreAsync(bool allowNetwork)
    {
        if (StartupStopped) return;
        // This shell now belongs to a real run whose network policy App has declared, which is what lets the
        // manual refresh above honour the offline switch. A shell built straight into a test and driven against
        // an injected refresher never starts, and has no policy for the switch to speak for.
        _started = true;
        var data = App.Api.Configured
            ? await DataBootstrap.ReadApiCoreAsync(App, () => new StartData(App.Db.GetAllGroups().Count, App.Db.GetSettings()))
            : await RunAsync(() => new StartData(App.Db.GetAllGroups().Count, App.Db.GetSettings()), "startup");
        if (StartupStopped) return;
        if (data is null)
        {
            Current = new ErrorStateViewModel(App, null, () => StartAsync(allowNetwork));
            return;
        }
        if (App.Api.Configured)
        {
            var result = await DataBootstrap.RunApiAsync(App, allowNetwork);
            if (StartupStopped) return;
            if (!result.HasData)
            {
                Current = new ErrorStateViewModel(App, result.Error, () => StartAsync(allowNetwork));
                return;
            }
            if (result.Stale && result.Error is not null) App.Toasts.Warn(result.Error);
        }
        else if (data.GroupCount == 0)
        {
            Current = new LoadingViewModel(App);
            // Network outside the gate, parse + SQLite inside — the same split RefreshScheduleAsync uses. The
            // empty-database bootstrap was the last path that downloaded while holding the gate, so a first
            // launch behind a dead network parked every other Core call behind the HTTP timeout.
            var fetched = allowNetwork ? await DataBootstrap.FetchAsync(App) : default;
            if (StartupStopped) return;
            var result = await RunAsync(() => DataBootstrap.RunAsync(App, fetched.Xml, fetched.Error), "bootstrap");
            if (StartupStopped) return;
            if (result is null || !result.HasData)
            {
                Current = new ErrorStateViewModel(App, result?.Error, () => StartAsync(allowNetwork));
                return;
            }
            if (result.Stale && result.Error is not null) App.Toasts.Warn($"{T("stale").Trim(' ', '·')}: {result.Error}");
        }
        try
        {
            if (StartupStopped) return;
            await RunAsync(() => { if (!StartupStopped) App.Homework.RecomputeAllStatuses(); }, "homework statuses");
            if (StartupStopped) return;
            await RefreshGroupCardAsync();
            if (StartupStopped) return;
            DetachSection(SectionKey.Schedule); // rebuild against fresh data
            NavigateTo(SectionKey.Schedule);
            await Section<ScheduleViewModel>(SectionKey.Schedule).InitializeAsync();
            if (StartupStopped) return;
            await UpdateHomeworkBadgeAsync();
            if (StartupStopped) return;
            if (allowNetwork)
            {
                if (data.GroupCount > 0 && ShouldAutoCheck(data.Settings, DateTime.UtcNow)) _ = RefreshScheduleAsync(force: false, quiet: true);
                StartAutoCheck();
                _ = Updates.RunStartupFlowAsync(); // silent self-update; every step of it swallows its own failures
            }
        }
        catch (Exception ex)
        {
            App.Log.Error("startup", ex);
            if (StartupStopped) return;
            Current = new ErrorStateViewModel(App, ex.Message, () => StartAsync(allowNetwork));
        }
    }

    private sealed record PickerData(List<Group> Groups, string? CurrentId);

    [RelayCommand]
    private async Task OpenGroupPickerAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
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
            App.Api.Invalidate();
            if (!App.Api.Configured) App.Homework.RecomputeAllStatuses();
        }, "group");
        if (!saved) return;
        if (App.Api.Configured)
        {
            await EnsureApiNeedsAsync();
            await RunAsync(() =>
            {
                if (new Vograph.Core.Services.TimetableApiCache(App.Db).Read(chosen.Id) is not null) App.Homework.RecomputeAllStatuses();
            }, "group homework");
        }
        await RefreshGroupCardAsync(); // before RaiseGroupChanged: sections read GroupName while they react
        RaiseGroupChanged();
        await ShowUnavailableApiSelectionAsync();
        await UpdateHomeworkBadgeAsync(); // another group means other lessons, so other due dates
        App.Toasts.Ok(T("savedOk"));
    }

    private sealed record CardData(Settings Settings, Group? Group, bool SourceStale);

    private CardData ReadCard()
    {
        var s = App.Db.GetSettings();
        return new CardData(s, string.IsNullOrEmpty(s.MyGroupId) ? null : App.Db.GetGroup(s.MyGroupId), App.Api.SourceStale);
    }

    /// <summary>Re-reads settings + group under the Core gate, then updates the card.</summary>
    public async Task RefreshGroupCardAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var data = await RunAsync(ReadCard, "group card");
        if (data is not null && operation.IsCurrent) ApplyGroupCard(data);
    }

    private void ApplyGroupCard(CardData data)
    {
        var settings = data.Settings;
        var group = data.Group;
        if (group is null)
        {
            GroupName = T("noGroup");
            GroupSubtitle = T("noGroupHint");
            GroupRailLabel = "—";
            return;
        }
        // The shell's injected clock, not the machine's: the card is the one place that still read DateTime
        // directly, which made its parity and its date depend on the day the suite happened to run (the import
        // test had to recompute its own expectation from the same real inputs to say anything at all).
        var now = Clock();
        var today = now.Date;
        var isOdd = ParityCodes.IsOdd(today, settings);
        var culture = CultureInfo.GetCultureInfo(App.Loc.Language == "en" ? "en-US" : "ru-RU");
        GroupName = group.Name;
        GroupRailLabel = GroupCardLogic.RailLabel(group.Name);
        GroupSubtitle = $"{T("parityWeek", App.I18n.FormatParity(isOdd))} · {today.ToString(App.Loc.Language == "en" ? "MMM d" : "d MMM", culture)}";
        // LastFetchedAt is stored in UTC and Stale compares against UTC; the default clock is DateTime.Now, so
        // this is the same instant it always was, only sourced from the clock a test can pin.
        var (stale, warn) = GroupCardLogic.Stale(settings.LastFetchedAt, now.ToUniversalTime(), App.Loc);
        StaleText = data.SourceStale ? "Данные расписания могут быть устаревшими" : stale;
        StaleWarn = warn || data.SourceStale;
    }

    /// <summary>Sealed type: 'internal' rather than 'protected' so later dialogs in this assembly can raise it without CS0628.
    /// Pure event invocation: OpenGroupPickerAsync awaits UpdateHomeworkBadgeAsync itself right after raising.</summary>
    internal void RaiseGroupChanged() { if (CanPublish) GroupChanged?.Invoke(); }
}
