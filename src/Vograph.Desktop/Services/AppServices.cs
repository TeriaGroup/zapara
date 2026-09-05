using Vograph.Core.Models;
using Vograph.Core.Services;

namespace Vograph.Desktop.Services;

/// <summary>Composition root. One instance per process; no DI container on purpose.</summary>
public sealed class AppServices : IDisposable
{
    public string DataDir { get; }
    public Database Db { get; }
    public I18nService I18n { get; }
    public Loc Loc { get; }
    public ParserService Parser { get; }
    public ScheduleService Schedule { get; }
    public OverrideService Overrides { get; }
    public HomeworkService Homework { get; }
    public IntersectionService Intersections { get; }
    public NotificationService Notifications { get; }
    public MapService Maps { get; }
    public LecturerService Lecturers { get; }
    public SyncService Sync { get; }
    public AutoUpdateService AutoUpdate { get; }
    public UiPrefs Prefs { get; }
    public ToastService Toasts { get; }
    public AppLog Log { get; }

    /// <summary>Needs a live Application; assigned by App at startup (or by UI tests). Null in plain unit tests.</summary>
    public ThemeService? Theme { get; set; }

    /// <summary>Core's SqliteConnection is not thread-safe: every background Core call goes through this gate.</summary>
    public SemaphoreSlim CoreGate { get; } = new(1, 1);

    private AppServices(string dataDir)
    {
        DataDir = dataDir;
        Directory.CreateDirectory(dataDir);
        Log = new AppLog(Path.Combine(dataDir, "logs"));
        Db = new Database(Path.Combine(dataDir, "vograph.db"));
        Parser = new ParserService(Db); // also registers the code-pages encoding provider
        var settings = Db.GetSettings();
        I18n = new I18nService(settings.Language ?? "ru");
        Loc.Init(I18n);
        Loc = Loc.Current;
        Schedule = new ScheduleService(Db);
        Overrides = new OverrideService(Db);
        Homework = new HomeworkService(Db);
        Intersections = new IntersectionService(Db);
        Notifications = new NotificationService(Db, Overrides, Homework, Schedule, I18n);
        Maps = new MapService(Db, Schedule);
        Lecturers = new LecturerService(Db); // loads lazily from the Teachers section (stage 2)
        Sync = new SyncService(Db);
        AutoUpdate = new AutoUpdateService();
        Prefs = UiPrefs.Load(Path.Combine(dataDir, "ui.json"), ex => Log.Error("prefs", ex));
        Toasts = new ToastService();
    }

    public static AppServices Create(string dataDir) => new(dataDir);

    /// <summary>Always re-read: Core services write settings behind our back (refresh, homework).</summary>
    public Settings Settings => Db.GetSettings();

    public void Dispose()
    {
        Db.Dispose();
        CoreGate.Dispose();
    }
}
