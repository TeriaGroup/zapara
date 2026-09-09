using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Core.Services.Sync;
using Vograph.Desktop.Features.Maps;
using Vograph.Desktop.Features.Teachers;
using Vograph.Desktop.Services.Profiles;

namespace Vograph.Desktop.Services;

/// <summary>Database-bound graph. Shared holds installation-scoped state across profile transitions.</summary>
public sealed partial class AppServices : IDisposable
{
    public ProfileDescriptor Profile { get; }
    public SharedProfileRuntime Shared { get; }
    public ProfileWorkLifetime Work { get; } = new();
    public bool IsClosed => _disposed;
    public string DataDir { get; }
    public Database Db { get; }
    public PrivateSyncOutbox Outbox { get; }
    public PrivateSyncCoordinator? PrivateSync { get; }
    public I18nService I18n { get; }
    public Loc Loc { get; }
    public ParserService Parser { get; }
    public ScheduleService Schedule { get; }
    public OverrideService Overrides { get; }
    public HomeworkService Homework { get; }
    public IntersectionService Intersections { get; }
    public NotificationService Notifications { get; }
    public MapService Maps { get; }
    public SyncService Sync { get; }
    public AutoUpdateService AutoUpdate { get; }
    public UiPrefs Prefs { get; }
    public MotionSettings Motion { get; }
    public ToastService Toasts { get; }
    public AppLog Log { get; }

    /// <summary>Cache-first lecturer directory; settable so tests point it at local files that cannot exist.</summary>
    public LecturerStore Lecturers { get; set; }

    /// <summary>Network half of timetable refreshes; settable so tests script the server with a FakeHttpHandler.</summary>
    public ScheduleRefresher Refresher { get; set; }
    public ApiRefreshCoordinator Api { get; }

    /// <summary>Plan images on disk; settable so tests serve a generated PNG instead of the real maps cache.</summary>
    public IMapFiles MapFiles { get; set; }

    /// <summary>Browser / Explorer; settable so tests record what would have been opened instead of opening it.</summary>
    public ILauncherService Launcher { get; set; }

    /// <summary>JSON Save/Open pickers; settable so tests script the picked path instead of opening an OS dialog.</summary>
    public IFileDialogs FileDialogs { get; set; } = new NullFileDialogs();

    /// <summary>GitHub releases; settable so tests script the release instead of calling the API.</summary>
    public IUpdateSource UpdateSource { get; set; }

    /// <summary>The two daily lesson notifications; App starts the timer, Settings drives the times and the switch.</summary>
    public NotificationScheduler NotificationScheduler { get; }

    /// <summary>LAN sync host (:8765); App starts it when the preference is on, the Settings switch toggles it.
    /// Settable so tests drive a loopback listener on a free port instead of binding every interface on 8765.</summary>
    public LanSyncServer LanSync { get; set; }

    /// <summary>Needs a live Application; assigned by App at startup (or by UI tests). Null in plain unit tests.</summary>
    public ThemeService? Theme { get => Shared.Theme; set => Shared.Theme = value; }

    /// <summary>Core's SqliteConnection is not thread-safe: every background Core call goes through this gate.</summary>
    public SemaphoreSlim CoreGate { get; } = new(1, 1);

    /// <summary>Process-wide network switch; tests set this false. Sections that can reach the network consult it.</summary>
    private bool _allowNetwork = true;
    public bool AllowNetwork
    {
        get => _allowNetwork;
        set { if (_allowNetwork != value) { _allowNetwork = value; Api?.NetworkPolicyChanged(value); } }
    }

    private readonly string? _apiBaseUrl;
    private readonly Func<Uri, TimetableApiClient>? _apiFactory;
    private AppServices(ProfileDescriptor profile, SharedProfileRuntime? shared, Func<bool>? systemAnimations, string? apiBaseUrl, Func<Uri, TimetableApiClient>? apiFactory)
    {
        Profile = profile;
        var dataDir = profile.GlobalDataDir;
        DataDir = dataDir;
        Directory.CreateDirectory(dataDir);
        Db = new Database(profile.DatabasePath);
        Outbox = new PrivateSyncOutbox(Db, enabled: !profile.IsGuest);
        Db.PrivateOutbox = Outbox;
        try
        {
            Parser = new ParserService(Db); // also registers the code-pages encoding provider
            var settings = Db.GetSettings();
            Shared = shared ?? new SharedProfileRuntime(dataDir, settings.Language ?? "ru", systemAnimations);
            Log = Shared.Log;
            I18n = Shared.I18n;
            Loc = Shared.Loc;
            Schedule = new ScheduleService(Db);
            Overrides = new OverrideService(Db, Outbox);
            Homework = new HomeworkService(Db, Outbox);
            Intersections = new IntersectionService(Db);
            Notifications = new NotificationService(Db, Overrides, Homework, Schedule, I18n);
            Maps = new MapService(Db, Schedule, Path.Combine(dataDir, "maps"), Path.Combine(AppContext.BaseDirectory, "maps"));
            MapFiles = new MapFiles(Maps, Log, () => AllowNetwork);
            Launcher = new NullLauncher(Log); // App swaps in AvaloniaLauncher once the window exists
            Lecturers = new LecturerStore(new LecturerService(Db, Path.Combine(dataDir, "TimetableLecturer50.xml"), Path.Combine(AppContext.BaseDirectory, "TimetableLecturer50.xml")), Log); // parsed lazily by the Teachers section
            Sync = new SyncService(Db);
            AutoUpdate = new AutoUpdateService();
            UpdateSource = new GitHubUpdateSource(AutoUpdate);
            Prefs = Shared.Prefs;
            Motion = Shared.Motion;
            Refresher = new ScheduleRefresher();
            Toasts = new ToastService(canPublish: () => Work.CanPublish);
            NotificationScheduler = new NotificationScheduler(this);
            LanSync = new LanSyncServer(this);
            _apiBaseUrl = apiBaseUrl ?? Environment.GetEnvironmentVariable("VOGRAPH_API_BASE_URL");
            _apiFactory = apiFactory;
            Api = new ApiRefreshCoordinator(this, _apiBaseUrl, apiFactory);
            PrivateSync = profile.IsGuest ? null : new PrivateSyncCoordinator(this);
        }
        catch
        {
            PrivateSync?.Dispose();
            Api?.Dispose();
            Refresher?.Dispose();
            NotificationScheduler?.Dispose();
            LanSync?.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(Db.Connection);
            Db.Dispose();
            CoreGate.Dispose();
            throw;
        }
    }

    /// <param name="systemAnimations">The Windows «reduce motion» reader; tests pin it so frames never animate.</param>
    public static AppServices Create(string dataDir, Func<bool>? systemAnimations = null, string? apiBaseUrl = null,
        Func<Uri, TimetableApiClient>? apiFactory = null) => new(ProfileDescriptor.Guest(dataDir), null, systemAnimations, apiBaseUrl, apiFactory);

    public AppServices CreateProfile(ProfileDescriptor profile)
    {
        if (profile.GlobalDataDir != Shared.GlobalDataDir) throw new ArgumentException("Другой каталог установки.");
        var candidate = new AppServices(profile, Shared, null, _apiBaseUrl, _apiFactory);
        candidate.AllowNetwork = AllowNetwork;
        candidate.Launcher = Launcher;
        candidate.FileDialogs = FileDialogs;
        return candidate;
    }

    /// <summary>Always re-read: Core services write settings behind our back (refresh, homework).</summary>
    public Settings Settings => Db.GetSettings();

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return; // the bounded Wait below would throw on a second pass
        _disposed = true;
        PrivateSync?.Dispose();
        Api.Dispose();
        Refresher.Dispose();
        NotificationScheduler.Dispose();
        LanSync.Dispose();
        // Take the gate before the SQLite connection goes: a gated Core call caught mid-query used to fault
        // inside SqliteConnection.Dispose. The gate is deliberately NOT released — disposal follows it, and
        // callers queued behind it land on the ObjectDisposedException path in ViewModelBase.GatedAsync.
        if (!CoreGate.Wait(TimeSpan.FromSeconds(2))) Log.Warn("shutdown: a Core call still holds the gate after 2 s, closing the database anyway");
        Db.Dispose();
        CoreGate.Dispose();
    }
}
