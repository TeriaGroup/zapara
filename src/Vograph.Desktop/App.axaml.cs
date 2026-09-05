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
            window.Opened += async (_, _) => await shell.StartAsync();
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => services.Dispose();
            services.Log.Info("desktop started");
        }
        base.OnFrameworkInitializationCompleted();
    }
}
