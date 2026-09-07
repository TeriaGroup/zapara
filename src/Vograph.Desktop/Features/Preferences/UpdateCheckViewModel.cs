using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
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
        _updatesDir = updatesDir ?? Path.Combine(App.DataDir, "updates");
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

    /// <summary>Where downloaded release zips (and their .attempted / .part companions) live; tests point it at their
    /// own scratch dir via the constructor and read it back to inspect what DownloadAsync/CleanupAsync left behind.</summary>
    public string UpdatesDir => _updatesDir;

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

    /// <summary>403/429 from GitHub is a quota, not a bug — say so (Android 1.2.18 wording); anything else gets the caller's wording with the message.</summary>
    public static string Friendly(Exception ex, Loc loc, string fallbackKey = "updFailWith")
    {
        var limited = ex is HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests }
                      || ex.Message.Contains("403") || ex.Message.Contains("429");
        return limited ? loc.T("updRateLimited") : loc.T(fallbackKey, ex.Message);
    }

    /// <summary>True when a newer release exists. Never throws.</summary>
    public async Task<bool> CheckAsync()
    {
        if (IsChecking) return false;
        State = UpdateState.Checking;
        // Every check starts from a clean slate: a release found last time must not stay installable after
        // a later «up to date» or a failure (T11 #3).
        LatestTag = null;
        PublishedText = null;
        _zipUrl = null;
        _zipPath = null;
        Progress = -1;
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
        var zip = Path.Combine(_updatesDir, $"ZAPARA_{SafeTag(LatestTag)}_win-x64.zip");
        if (File.Exists(zip) && new FileInfo(zip).Length > 0)
        {
            if (LooksLikeZip(zip))
            {
                _zipPath = zip;
                Progress = 1;
                State = UpdateState.Ready;
                StatusText = T("updDownloaded", LatestTag);
                return true;
            }
            // Left over from a previous, bad download (truncated, an HTML error page saved as .zip): drop it and
            // fall through to a fresh download instead of handing the installer a file it cannot unpack.
            try { File.Delete(zip); } catch (IOException ex) { App.Log.Error("update zip delete", ex); }
        }
        State = UpdateState.Downloading;
        Progress = -1;
        StatusText = T("updDownloading", LatestTag);
        try
        {
            await App.UpdateSource.DownloadAsync(_zipUrl, zip, new Progress<double>(p => Progress = p));
            if (!await Task.Run(() => LooksLikeZip(zip)))
            {
                App.Log.Warn($"update: {zip} is not a release archive, deleting it");
                try { File.Delete(zip); } catch (IOException ex) { App.Log.Error("update zip delete", ex); }
                Fail(T("updBadZip"));
                return false;
            }
            _zipPath = zip;
            State = UpdateState.Ready;
            StatusText = T("updDownloaded", LatestTag);
            return true;
        }
        catch (Exception ex)
        {
            App.Log.Error("update download", ex);
            Fail(Friendly(ex, App.Loc, "updDownloadFail"));
            return false;
        }
    }

    private int _installing;

    /// <summary>Available → download → Ready → hand the zip to the installer (batch + shutdown). Writes the
    /// "attempted" marker first (R45). The Interlocked guard covers direct callers as well as the command (T11 #9).</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task InstallAsync()
    {
        if (Interlocked.Exchange(ref _installing, 1) == 1) return;
        try
        {
            if (State == UpdateState.Available && !await DownloadAsync()) return;
            if (State != UpdateState.Ready || _zipPath is null) return;
            WriteAttemptedMarker(_zipPath);
            try { Installer(_zipPath); }
            catch (Exception ex)
            {
                App.Log.Error("update apply", ex);
                Fail(T("updApplyFail", ex.Message));
            }
        }
        finally
        {
            Interlocked.Exchange(ref _installing, 0);
        }
    }

    [RelayCommand] private Task Check() => CheckAsync();
    [RelayCommand] private Task OpenReleases() => App.Launcher.OpenUrlAsync(HtmlUrl ?? SettingsViewModel.ReleasesUrl);

    /// <summary>Spec §6: the silent startup update toasts «Обновляюсь до …» and restarts without a dialog.
    /// R45: if this tag's zip already carries an "attempted" marker — a previous silent run got this far but the
    /// app is back, meaning the install never actually completed (locked directory, AV, disk full) — the check
    /// still runs, so the sidebar item/badge and the Settings card stay the visible route, but the toast and the
    /// installer are skipped. Without this, a relaunch that can never unpack would toast and shut down forever.</summary>
    public async Task RunStartupFlowAsync()
    {
        await CleanupAsync();
        var s = await RunAsync(() => App.Db.GetSettings(), "settings");
        if (s is null || !s.AutoUpdate) return;
        _suppress = true;
        AutoUpdate = true;
        _suppress = false;
        if (!await CheckAsync()) return;
        if (!await DownloadAsync()) return;
        if (_zipPath is not null && File.Exists(AttemptedMarkerPath(_zipPath))) return;
        App.Toasts.Info(T("updUpdatingTo", LatestTag!));
        await Delay(TimeSpan.FromSeconds(2));
        await InstallAsync();
    }

    private static string AttemptedMarkerPath(string zipPath) => zipPath + ".attempted";

    /// <summary>Bookkeeping only: a failed write is logged, never fatal to the install itself.</summary>
    private void WriteAttemptedMarker(string zipPath)
    {
        try
        {
            File.WriteAllText(AttemptedMarkerPath(zipPath), $"{LatestTag}\t{_clock():O}");
        }
        catch (Exception ex)
        {
            App.Log.Error("update marker", ex);
        }
    }

    private void Fail(string text)
    {
        State = UpdateState.Failed;
        StatusText = text;
    }

    private string Stamp() => _clock().ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A remote tag becomes part of a file name: anything the file system would reject (or «..») becomes «_».</summary>
    public static string SafeTag(string tag)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(tag.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        while (safe.Contains("..")) safe = safe.Replace("..", "_");
        return safe;
    }

    /// <summary>The batch unpacks whatever it is given over the install directory: make sure it is an archive and that
    /// Vograph.exe is inside before a shutdown is triggered on its account.</summary>
    public static bool LooksLikeZip(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 1024) return false;
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Any(e => e.FullName.Equals("Vograph.exe", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith("/Vograph.exe", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Old downloads: zips (and their .attempted / .part companions) of this version or older are gone —
    /// they were installed or superseded. Newer ones stay (a download the user has not applied yet). Never throws.</summary>
    public Task CleanupAsync() => Task.Run(() =>
    {
        try
        {
            if (!Directory.Exists(_updatesDir)) return;
            foreach (var file in Directory.GetFiles(_updatesDir))
            {
                var name = Path.GetFileName(file);
                var m = Regex.Match(name, @"^ZAPARA_(?<tag>windows-v[\d.]+)_win-x64\.zip(\.attempted|\.part)?$");
                var stale = m.Success ? !AutoUpdateService.IsNewer(m.Groups["tag"].Value, AppVersion.Tag) : File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-30);
                if (!stale) continue;
                try { File.Delete(file); App.Log.Info($"update: removed {name}"); }
                catch (IOException ex) { App.Log.Warn($"update: could not remove {name}: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            App.Log.Error("update cleanup", ex);
        }
    });
}

/// <summary>The download bar is a plain Border: its filled part is Progress (0..1) of the 320px track.</summary>
public static class UpdateConverters
{
    public static readonly IValueConverter ProgressWidth = new FuncValueConverter<double, double>(p => Math.Clamp(p, 0, 1) * 320);
}
