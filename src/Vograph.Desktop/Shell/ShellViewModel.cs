using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;
using Vograph.Desktop.Dialogs;
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
    }

    /// <summary>Sealed type: 'internal' rather than 'protected' so later dialogs in this assembly can raise it without CS0628.</summary>
    internal void RaiseGroupChanged() => GroupChanged?.Invoke();
}
