using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Models;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Preferences;

public sealed partial class SettingsViewModel : ViewModelBase
{
    public const string ReleasesUrl = "https://github.com/0NiLle0/zapara/releases";
    public const string TimetableSourceUrl = "https://voenmeh.ru/obrazovanie/timetables/";
    public const string MapsSourceUrl = "https://voenmeh.ru/openmap/";

    private readonly ShellViewModel _shell;
    private readonly Func<DateTime> _clock;
    private readonly Action _reload;
    private readonly PropertyChangedEventHandler _onShell;
    private readonly Action _onTheme;
    private bool _suppress;
    private int _version;

    public SettingsViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _themeItems = BuildThemeItems();
        _languageItems = new[] { T("langRu"), T("langEn") };
        _themeIndex = app.Theme is { } t ? (int)t.Choice : (int)app.Prefs.Theme;
        _languageIndex = app.Loc.Language == "en" ? 1 : 0;
        _compactSidebar = shell.SidebarCollapsed;
        _animations = app.Prefs.Animations;
        _reload = () => _ = LoadAsync();
        _onShell = (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.SidebarCollapsed)) { _suppress = true; CompactSidebar = shell.SidebarCollapsed; _suppress = false; }
            if (e.PropertyName == nameof(ShellViewModel.IsRefreshing)) IsRefreshing = shell.IsRefreshing;
        };
        _onTheme = () => { _suppress = true; ThemeIndex = (int)App.Theme!.Choice; _suppress = false; };
        shell.PropertyChanged += _onShell;
        shell.GroupChanged += _reload;
        shell.ScheduleChanged += _reload;
        app.Loc.LanguageChanged += Relabel;
        if (app.Theme is { } theme) theme.Changed += _onTheme; // mirrors the sidebar's quick-toggle back into ThemeIndex
    }

    public override void Detach()
    {
        _shell.PropertyChanged -= _onShell;
        _shell.GroupChanged -= _reload;
        _shell.ScheduleChanged -= _reload;
        App.Loc.LanguageChanged -= Relabel;
        if (App.Theme is { } theme) theme.Changed -= _onTheme;
    }

    public override Task ActivateAsync() => LoadAsync();

    public string Title => T("navSettings");

    // ---- Appearance ----
    [ObservableProperty] private IList<string> _themeItems;
    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private IList<string> _languageItems;
    [ObservableProperty] private int _languageIndex;
    [ObservableProperty] private bool _compactSidebar;
    [ObservableProperty] private bool _animations;

    private IList<string> BuildThemeItems() => new[] { T("themeSystem"), T("themeLight"), T("themeDark") };

    partial void OnThemeIndexChanged(int value)
    {
        if (_suppress) return;
        var choice = (ThemeChoice)Math.Clamp(value, 0, 2);
        if (App.Theme is { } theme) theme.Apply(choice);
        else { App.Prefs.Theme = choice; App.Prefs.Save(); }
    }

    partial void OnLanguageIndexChanged(int value)
    {
        if (_suppress) return;
        var lang = value == 1 ? "en" : "ru";
        App.Loc.SetLanguage(lang); // relabels every section at once
        _ = RunAsync(() => { var s = App.Db.GetSettings(); s.Language = lang; App.Db.SaveSettings(s); }, "language");
    }

    partial void OnCompactSidebarChanged(bool value)
    {
        if (!_suppress) _shell.SidebarCollapsed = value;
    }

    partial void OnAnimationsChanged(bool value)
    {
        if (_suppress) return;
        App.Prefs.Animations = value;
        App.Prefs.Save();
    }

    // ---- Schedule ----
    [ObservableProperty] private string _groupName = "—";
    [ObservableProperty] private bool _parityInvert;
    [ObservableProperty] private string _updatedText = "";
    [ObservableProperty] private string _autoCheckText = "";
    [ObservableProperty] private bool _isRefreshing;

    partial void OnParityInvertChanged(bool value)
    {
        if (_suppress) return;
        _ = SaveInvertAsync(value);
    }

    private async Task SaveInvertAsync(bool value)
    {
        var ok = await RunAsync(() =>
        {
            var s = App.Db.GetSettings();
            s.ParityInvert = value;
            App.Db.SaveSettings(s);
            App.Homework.RecomputeAllStatuses();
        }, "parity");
        if (ok) _shell.RaiseScheduleChanged();
    }

    [RelayCommand] private Task ChangeGroup() => _shell.OpenGroupPickerCommand.ExecuteAsync(null);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Refresh()
    {
        await _shell.RefreshScheduleAsync(force: true, quiet: false);
        await LoadAsync();
    }

    private sealed record SettingsData(Settings Settings, string? GroupName);

    public async Task LoadAsync()
    {
        var version = ++_version;
        var data = await RunAsync(() =>
        {
            var s = App.Db.GetSettings();
            return new SettingsData(s, string.IsNullOrEmpty(s.MyGroupId) ? null : App.Db.GetGroup(s.MyGroupId)?.Name);
        }, "settings");
        if (data is null || version != _version) return;
        _suppress = true;
        GroupName = data.GroupName ?? T("noGroup");
        ParityInvert = data.Settings.ParityInvert;
        ThemeIndex = App.Theme is { } t ? (int)t.Choice : (int)App.Prefs.Theme;
        LanguageIndex = App.Loc.Language == "en" ? 1 : 0;
        CompactSidebar = _shell.SidebarCollapsed;
        Animations = App.Prefs.Animations;
        _suppress = false;
        UpdatedText = T("updatedChip", Stamp(data.Settings.LastFetchedAt));
        AutoCheckText = T("setAutoCheckAt", Stamp(data.Settings.LastAutoCheckAt));
        IsRefreshing = _shell.IsRefreshing;
    }

    /// <summary>ISO UTC → «06.09 15:00» local, or «ещё не было». UpdatedText reuses the sidebar group card's
    /// "updatedChip" template (identical "обновлено {0}" / "updated {0}" text in both languages) instead of a
    /// second key with the same value; only the argument's own format (date+time here vs. date-only there) differs.</summary>
    private string Stamp(string? iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at.ToLocalTime().ToString(App.Loc.Language == "en" ? "MM-dd HH:mm" : "dd.MM HH:mm", CultureInfo.InvariantCulture)
            : T("setNever");

    // ---- About ----
    public string VersionText => T("setVersion", AppVersion.Tag);
    [RelayCommand] private Task OpenReleases() => App.Launcher.OpenUrlAsync(ReleasesUrl);
    [RelayCommand] private Task OpenTimetableSource() => App.Launcher.OpenUrlAsync(TimetableSourceUrl);
    [RelayCommand] private Task OpenMapsSource() => App.Launcher.OpenUrlAsync(MapsSourceUrl);
    [RelayCommand] private Task OpenDataFolder() => App.Launcher.OpenFolderAsync(App.DataDir);

    private void Relabel()
    {
        _suppress = true;
        ThemeItems = BuildThemeItems();
        LanguageItems = new[] { T("langRu"), T("langEn") };
        _suppress = false;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(VersionText));
        _ = LoadAsync();
    }
}
