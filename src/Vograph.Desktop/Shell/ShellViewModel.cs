using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        SidebarCollapsed = app.Prefs.SidebarCollapsed;
        app.Loc.LanguageChanged += () =>
        {
            foreach (var s in AllSections) s.RefreshLabel();
            RefreshGroupCard();
        };
        RefreshGroupCard();
        NavigateTo(SectionKey.Schedule);
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

    private NavSection Make(SectionKey key, string labelKey, string iconKey) => new(key, labelKey, iconKey, NavigateCommand);

    /// <summary>Later tasks replace the placeholder factory of a section with the real one.</summary>
    public void Register(SectionKey key, Func<ViewModelBase> factory)
    {
        _factories[key] = factory;
        _sections.Remove(key);
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

    /// <summary>Startup: loading state → bootstrap (network unless disabled) → schedule or error state.</summary>
    public async Task StartAsync(bool allowNetwork = true)
    {
        Current = new LoadingViewModel(App);
        // Network fetch + XML parse + SQLite writes: all under the Core gate, never on the UI thread.
        var result = await RunAsync(() => DataBootstrap.RunAsync(App, allowNetwork), "bootstrap");
        if (result is null)
        {
            Current = new ErrorStateViewModel(App, null, () => StartAsync(allowNetwork));
            return;
        }
        if (!result.HasData)
        {
            Current = new ErrorStateViewModel(App, result.Error, () => StartAsync(allowNetwork));
            return;
        }
        await RunAsync(() => App.Homework.RecomputeAllStatuses(), "homework statuses");
        RefreshGroupCard();
        _sections.Remove(SectionKey.Schedule); // rebuild against fresh data
        NavigateTo(SectionKey.Schedule);
        await Section<ScheduleViewModel>(SectionKey.Schedule).InitializeAsync();
        if (result.Stale && result.Error is not null) App.Toasts.Warn($"{T("stale")}: {result.Error}");
        if (allowNetwork) App.AutoRefresh.Start();
    }

    [RelayCommand]
    private async Task OpenGroupPickerAsync()
    {
        var groups = await RunAsync(() => App.Db.GetAllGroups(), "groups");
        if (groups is null) return;
        var dlg = new GroupPickerDialogViewModel(groups, App.Settings.MyGroupId);
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
        RefreshGroupCard();
        RaiseGroupChanged();
        App.Toasts.Ok(T("savedOk"));
    }

    public void RefreshGroupCard()
    {
        var settings = App.Settings;
        var group = string.IsNullOrEmpty(settings.MyGroupId) ? null : App.Db.GetGroup(settings.MyGroupId);
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
