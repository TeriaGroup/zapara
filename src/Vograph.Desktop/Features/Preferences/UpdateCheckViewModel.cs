using System.Globalization;
using System.Net;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;
using Vograph.Desktop.Services;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Preferences;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Ready, Failed }

/// <summary>One update state for the whole app: the Settings card, the sidebar «Обновление» item and the silent
/// startup flow all read it. Network calls go straight to IUpdateSource (outside the Core gate); only the AutoUpdate
/// setting touches SQLite.</summary>
public sealed partial class UpdateCheckViewModel : ViewModelBase
{
    private readonly Func<DateTime> _clock;
    private readonly string _updatesDir;
    private string? _zipUrl;
    private string? _zipPath;
    private bool _suppress;

    public UpdateCheckViewModel(AppServices app, Func<DateTime>? clock = null, string? updatesDir = null) : base(app)
    {
        _clock = clock ?? (() => DateTime.Now);
        _updatesDir = updatesDir ?? AutoUpdateService.UpdatesDir;
        _statusText = T("updIdle");
        Installer = zip => UpdateRunner.Apply(zip, AppContext.BaseDirectory, Shutdown ?? (() => { }));
        Delay = span => Task.Delay(span);
    }

    /// <summary>Replaced in tests; the default writes the batch file and shuts the app down.</summary>
    public Action<string> Installer { get; set; }
    public Action? Shutdown { get; set; }
    /// <summary>The pause that lets the «Обновляюсь до …» toast be seen before the restart.</summary>
    public Func<TimeSpan, Task> Delay { get; set; }
    public bool CheckedThisSession { get; private set; }

    [ObservableProperty] private UpdateState _state;
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private string? _latestTag;
    [ObservableProperty] private string? _publishedText;
    [ObservableProperty] private string? _htmlUrl;
    [ObservableProperty] private double _progress = -1;
    [ObservableProperty] private string _checkedAt = "";
    [ObservableProperty] private bool _autoUpdate = true;

    public bool IsAvailable => State is UpdateState.Available or UpdateState.Downloading or UpdateState.Ready;
    public bool IsChecking => State is UpdateState.Checking or UpdateState.Downloading;
    public bool CanInstall => State is UpdateState.Available or UpdateState.Ready;
    public bool IsDownloading => State == UpdateState.Downloading;
    public bool HasDeterminateProgress => Progress >= 0;
    public bool IsFailed => State == UpdateState.Failed;
    public string? BadgeText => IsAvailable ? "1" : null;

    partial void OnStateChanged(UpdateState value)
    {
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(IsChecking));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(BadgeText));
    }

    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(HasDeterminateProgress));

    partial void OnAutoUpdateChanged(bool value)
    {
        if (_suppress) return;
        _ = RunAsync(() => { var s = App.Db.GetSettings(); s.AutoUpdate = value; App.Db.SaveSettings(s); }, "auto-update");
    }

    public async Task LoadAsync()
    {
        var s = await RunAsync(() => App.Db.GetSettings(), "settings");
        if (s is null) return;
        _suppress = true;
        AutoUpdate = s.AutoUpdate;
        _suppress = false;
    }

    /// <summary>403/429 from GitHub is a quota, not a bug — say so (Android 1.2.18 wording).</summary>
    public static string Friendly(Exception ex, Loc loc)
    {
        var limited = ex is HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests }
                      || ex.Message.Contains("403") || ex.Message.Contains("429");
        return limited ? loc.T("updRateLimited") : loc.T("updFailWith", ex.Message);
    }

    /// <summary>True when a newer release exists. Never throws.</summary>
    public async Task<bool> CheckAsync(bool manual)
    {
        if (IsChecking) return false;
        State = UpdateState.Checking;
        StatusText = T("updChecking");
        AutoUpdateService.UpdateInfo? info;
        try
        {
            info = await App.UpdateSource.GetLatestAsync();
        }
        catch (Exception ex)
        {
            App.Log.Error("update check", ex);
            CheckedAt = Stamp();
            HtmlUrl = SettingsViewModel.ReleasesUrl;
            Fail(Friendly(ex, App.Loc));
            return false;
        }
        CheckedThisSession = true;
        CheckedAt = Stamp();
        if (info is null || string.IsNullOrEmpty(info.ZipUrl))
        {
            HtmlUrl = SettingsViewModel.ReleasesUrl;
            Fail(T("updNoReleases"));
            return false;
        }
        HtmlUrl = info.HtmlUrl;
        if (!AutoUpdateService.IsNewer(info.Tag, AppVersion.Tag))
        {
            State = UpdateState.UpToDate;
            StatusText = T("updUpToDate", AppVersion.Tag, CheckedAt);
            return false;
        }
        LatestTag = info.Tag;
        _zipUrl = info.ZipUrl;
        PublishedText = DateTime.TryParse(info.PublishedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var p) ? p.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : null;
        State = UpdateState.Available;
        StatusText = T("updAvailable", info.Tag);
        return true;
    }

    public async Task<bool> DownloadAsync()
    {
        if (State != UpdateState.Available || _zipUrl is null || LatestTag is null) return false;
        var zip = Path.Combine(_updatesDir, $"ZAPARA_{LatestTag}_win-x64.zip");
        if (File.Exists(zip) && new FileInfo(zip).Length > 0)
        {
            _zipPath = zip;
            Progress = 1;
            State = UpdateState.Ready;
            StatusText = T("updDownloaded", LatestTag);
            return true;
        }
        State = UpdateState.Downloading;
        Progress = -1;
        StatusText = T("updDownloading", LatestTag);
        try
        {
            await App.UpdateSource.DownloadAsync(_zipUrl, zip, new Progress<double>(p => Progress = p));
            _zipPath = zip;
            State = UpdateState.Ready;
            StatusText = T("updDownloaded", LatestTag);
            return true;
        }
        catch (Exception ex)
        {
            App.Log.Error("update download", ex);
            Fail(Friendly(ex, App.Loc));
            return false;
        }
    }

    /// <summary>Available → download → Ready → hand the zip to the installer (batch + shutdown).</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task InstallAsync()
    {
        if (State == UpdateState.Available && !await DownloadAsync()) return;
        if (State != UpdateState.Ready || _zipPath is null) return;
        try
        {
            Installer(_zipPath);
        }
        catch (Exception ex)
        {
            App.Log.Error("update apply", ex);
            Fail(Friendly(ex, App.Loc));
        }
    }

    [RelayCommand] private Task Check() => CheckAsync(manual: true);
    [RelayCommand] private Task OpenReleases() => App.Launcher.OpenUrlAsync(HtmlUrl ?? SettingsViewModel.ReleasesUrl);

    /// <summary>Spec §6: the silent startup update toasts «Обновляюсь до …» and restarts without a dialog.</summary>
    public async Task RunStartupFlowAsync()
    {
        var s = await RunAsync(() => App.Db.GetSettings(), "settings");
        if (s is null || !s.AutoUpdate) return;
        _suppress = true;
        AutoUpdate = true;
        _suppress = false;
        if (!await CheckAsync(manual: false)) return;
        if (!await DownloadAsync()) return;
        App.Toasts.Info(T("updUpdatingTo", LatestTag!));
        await Delay(TimeSpan.FromSeconds(2));
        await InstallAsync();
    }

    private void Fail(string text)
    {
        State = UpdateState.Failed;
        StatusText = text;
    }

    private string Stamp() => _clock().ToString("HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>The download bar is a plain Border: its filled part is Progress (0..1) of the 320px track.</summary>
public static class UpdateConverters
{
    public static readonly IValueConverter ProgressWidth = new FuncValueConverter<double, double>(p => Math.Clamp(p, 0, 1) * 320);
}
