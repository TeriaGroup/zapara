using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Avalonia.Markup.Xaml;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;

namespace Vograph.Desktop;

public partial class App : Application
{
    public AppServices? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Headless tests use a different lifetime and build their own services.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = AppServices.Create(AppPaths.DataDir);
            Services = services;
            Logger.Sink = new AvaloniaLogSink(services.Log);
            services.Theme = ThemeService.ForApplication(this, services.Prefs);

            var shell = new ShellViewModel(services);
            var window = new MainWindow { DataContext = shell };
            services.Launcher = new AvaloniaLauncher(() => window, services.Log);
            services.FileDialogs = new AvaloniaFileDialogs(() => window);
            services.NotificationScheduler.Start();
            if (services.Prefs.LanSync)
            {
                try { services.LanSync.Start(); }
                catch (Exception ex) { services.Log.Error("lan sync start", ex); services.Prefs.LanSync = false; services.Prefs.Save(); }
            }
            window.Opened += async (_, _) => await shell.StartAsync();
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => services.Dispose();
            services.Log.Info("desktop started");
        }
        base.OnFrameworkInitializationCompleted();
    }
}
