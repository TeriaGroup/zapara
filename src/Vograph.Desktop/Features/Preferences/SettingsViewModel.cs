using System.ComponentModel;
using System.Globalization;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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
    private readonly Action _onImported;
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
        _notificationsEnabled = app.Prefs.NotificationsEnabled;
        _lanSync = app.LanSync.IsRunning;
        _reload = () => _ = LoadAsync();
        _onImported = () => Dispatcher.UIThread.Post(() => { _shell.RaiseScheduleChanged(); _shell.RaiseHomeworkChanged(); });
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
        app.LanSync.Imported += _onImported; // a phone that pushed its data over the LAN must show up right away
        if (app.Theme is { } theme) theme.Changed += _onTheme; // mirrors the sidebar's quick-toggle back into ThemeIndex
    }

    public override void Detach()
    {
        _shell.PropertyChanged -= _onShell;
        _shell.GroupChanged -= _reload;
        _shell.ScheduleChanged -= _reload;
        App.Loc.LanguageChanged -= Relabel;
        App.LanSync.Imported -= _onImported;
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

    // ---- Notifications ----
    [ObservableProperty] private bool _notificationsEnabled;
    [ObservableProperty] private string _notifyTime1 = "20:00";
    [ObservableProperty] private string _notifyTime2 = "07:30";

    partial void OnNotificationsEnabledChanged(bool value)
    {
        if (_suppress) return;
        App.Prefs.NotificationsEnabled = value;
        App.Prefs.Save();
    }

    [RelayCommand]
    private async Task SaveTimes()
    {
        if (!NotificationScheduler.IsValidTime(NotifyTime1) || !NotificationScheduler.IsValidTime(NotifyTime2))
        {
            App.Toasts.Warn(T("notifBadTime"));
            return;
        }
        var (t1, t2) = (NotifyTime1.Trim(), NotifyTime2.Trim());
        var ok = await RunAsync(() => { var s = App.Db.GetSettings(); s.NotifyTime1 = t1; s.NotifyTime2 = t2; App.Db.SaveSettings(s); }, "notify times");
        if (ok) App.Toasts.Ok(T("notifSaved", t1, t2));
    }

    [RelayCommand] private async Task TestNotification() => await App.NotificationScheduler.ShowTestAsync(_clock());

    // ---- Sync ----
    [ObservableProperty] private Bitmap? _qrImage;
    [ObservableProperty] private bool _qrVisible;
    [ObservableProperty] private string _qrHint = "";
    [ObservableProperty] private bool _lanSync;
    [ObservableProperty] private string _lanAddress = "";
    private bool _qrViaServer; // which of the two hints the visible QR earned, so a language switch can redo it

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Export()
    {
        var path = await App.FileDialogs.SaveJsonAsync($"vograph-sync-{_clock().ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.json");
        if (path is null) return;
        if (await RunAsync(() => App.Sync.ExportToFile(path), "export"))
            App.Toasts.Ok(T("syncExported", Path.GetFileName(path)));
    }

    private sealed record ImportResult(int Overrides, int Homework, int Friends);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Import()
    {
        var path = await App.FileDialogs.OpenJsonAsync();
        if (path is null) return;
        string json;
        try { json = await File.ReadAllTextAsync(path, Encoding.UTF8); }
        catch (Exception ex)
        {
            App.Log.Error("import read", ex);
            App.Toasts.Error($"{T("errorTitle")}: {ex.Message}");
            return;
        }
        var result = await RunAsync(() =>
        {
            var (o, h, f) = App.Sync.ImportFromJson(json);
            App.Homework.RecomputeAllStatuses();
            return new ImportResult(o, h, f);
        }, "import");
        if (result is null) return;
        App.Toasts.Ok(T("importOk", result.Overrides, result.Homework, result.Friends));
        _shell.RaiseScheduleChanged();
        _shell.RaiseHomeworkChanged();
        await LoadAsync();
    }

    private sealed record QrData(string Path, bool ViaServer);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ToggleQr()
    {
        if (QrVisible)
        {
            QrVisible = false;
            QrImage?.Dispose();
            QrImage = null;
            return;
        }
        var qrPath = Path.Combine(App.DataDir, "sync-qr.png");
        var data = await RunAsync(() =>
        {
            var json = App.Sync.ExportToJson();
            var content = App.Sync.GenerateQrContent(json);
            App.Sync.SaveQrImage(content, qrPath);
            // Core keeps the QR itself under 1500 chars: a bigger export is published over the LAN server instead.
            return new QrData(qrPath, content.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        }, "qr");
        if (data is null) return;
        try { QrImage = await Task.Run(() => new Bitmap(data.Path)); }
        catch (Exception ex)
        {
            App.Log.Error("qr image", ex);
            App.Toasts.Error($"{T("errorTitle")}: {ex.Message}");
            return;
        }
        _qrViaServer = data.ViaServer;
        QrHint = T(_qrViaServer ? "syncQrServerHint" : "syncQrHint");
        QrVisible = true;
    }

    partial void OnLanSyncChanged(bool value)
    {
        if (_suppress) return;
        if (value)
        {
            try
            {
                App.LanSync.Start();
            }
            catch (Exception ex)
            {
                App.Log.Error("lan sync start", ex);
                App.Toasts.Error(App.LanSync.StartFailureText(ex));
                _suppress = true;
                LanSync = false;
                _suppress = false;
                return;
            }
            LanAddress = "";
            _ = ShowLanAddressAsync(); // the address needs DNS; it lands a moment after the switch
        }
        else
        {
            App.LanSync.Stop();
            LanAddress = "";
        }
        App.Prefs.LanSync = value;
        App.Prefs.Save();
    }

    /// <summary>«Адрес: …» for the running server. The host name behind it comes from a DNS lookup, so it is resolved
    /// off the UI thread and shown once it is known — the UI thread never waits on a resolver.</summary>
    private async Task ShowLanAddressAsync()
    {
        var address = await App.LanSync.ResolveAddressAsync(); // never throws: falls back to the loopback address
        if (App.LanSync.IsRunning) LanAddress = T("syncLanAddress", address);
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
        NotificationsEnabled = App.Prefs.NotificationsEnabled;
        NotifyTime1 = data.Settings.NotifyTime1 ?? "20:00";
        NotifyTime2 = data.Settings.NotifyTime2 ?? "07:30";
        LanSync = App.LanSync.IsRunning;
        _suppress = false;
        if (QrVisible) QrHint = T(_qrViaServer ? "syncQrServerHint" : "syncQrHint");
        UpdatedText = T("updatedChip", Stamp(data.Settings.LastFetchedAt));
        AutoCheckText = T("setAutoCheckAt", Stamp(data.Settings.LastAutoCheckAt));
        IsRefreshing = _shell.IsRefreshing;
        // Last, because it awaits: the address is resolved off the UI thread and cached by the server.
        if (App.LanSync.IsRunning) await ShowLanAddressAsync();
        else LanAddress = "";
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
