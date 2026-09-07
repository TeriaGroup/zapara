using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;

namespace Vograph.Desktop;

public partial class App : Application
{
    public AppServices? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    private IStyle? _motionStyles;

    /// <summary>The resource key Theme/Motion.axaml carries so SetMotion can pick it out of Application.Styles:
    /// Avalonia's XAML compiler inlines App.axaml's same-assembly StyleInclude into a plain Styles object, so there
    /// is no StyleInclude.Source left to match on.</summary>
    private const string MotionStylesKey = "Motion.Styles";

    /// <summary>Adds or removes Theme/Motion.axaml (every transition and looping animation). App calls it at startup and
    /// whenever MotionSettings flips; UI tests call it to keep frames deterministic.</summary>
    public void SetMotion(bool enabled)
    {
        _motionStyles ??= Styles.First(s => s is IResourceProvider { HasResources: true } p && p.TryGetResource(MotionStylesKey, null, out _));
        var present = Styles.Contains(_motionStyles);
        if (enabled && !present) Styles.Add(_motionStyles);
        else if (!enabled && present) Styles.Remove(_motionStyles);
    }

    /// <summary>The one log that works when AppServices (and its AppLog) could not be built: DataDir\logs, else %TEMP%.</summary>
    public static string WriteStartupError(Exception ex, string dataDir)
    {
        var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} STARTUP {ex}{Environment.NewLine}";
        try
        {
            var dir = Path.Combine(dataDir, "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "startup-error.log");
            File.AppendAllText(path, text);
            return path;
        }
        catch (Exception)
        {
            var path = Path.Combine(Path.GetTempPath(), "vograph-startup-error.log");
            try { File.AppendAllText(path, text); } catch (Exception) { /* nowhere left to write */ }
            return path;
        }
    }

    private static string SafeDataDir()
    {
        try { return AppPaths.DataDir; }
        catch (Exception) { return Path.GetTempPath(); }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Headless tests use a different lifetime and build their own services.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string dataDir;
            AppServices services;
            try
            {
                dataDir = AppPaths.DataDir;
                services = AppServices.Create(dataDir, MotionSettings.ReadSystemSetting);
            }
            catch (Exception ex)
            {
                dataDir = SafeDataDir();
                var log = WriteStartupError(ex, dataDir);
                desktop.MainWindow = new StartupErrorWindow { DataContext = new StartupError(ex.Message, dataDir, log) };
                base.OnFrameworkInitializationCompleted();
                return;
            }
            Services = services;
            Logger.Sink = new AvaloniaLogSink(services.Log);
            services.Theme = ThemeService.ForApplication(this, services.Prefs);
            SetMotion(services.Motion.Enabled);
            services.Motion.PropertyChanged += (_, _) => SetMotion(services.Motion.Enabled);

            var shell = new ShellViewModel(services);
            shell.Shutdown = () => desktop.Shutdown(); // the update batch waits for this process to exit
            var window = new MainWindow { DataContext = shell };
            services.Launcher = new AvaloniaLauncher(() => window, services.Log);
            services.FileDialogs = new AvaloniaFileDialogs(() => window);
            services.NotificationScheduler.Start();
            if (services.Prefs.LanSync)
            {
                try
                {
                    services.LanSync.Start();
                    _ = services.LanSync.ResolveAddressAsync(); // warms the address off the UI thread; never throws
                }
                catch (Exception ex)
                {
                    services.Log.Error("lan sync start", ex);
                    // The window is built but not shown yet: the toast waits in the queue and appears with it.
                    services.Toasts.Error(services.LanSync.StartFailureText(ex));
                    services.Prefs.LanSync = false;
                    services.Prefs.Save();
                }
            }
            window.Opened += async (_, _) => await shell.StartAsync();
            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                shell.Stop();      // timer and section subscriptions first
                services.Dispose(); // then the gate and SQLite
            };
            services.Log.Info("desktop started");
        }
        base.OnFrameworkInitializationCompleted();
    }
}
