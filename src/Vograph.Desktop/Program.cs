using Avalonia;

namespace Vograph.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Services.AppCulture.Apply(); // #38: до старта Avalonia, чтобы UI-поток получил ru-RU
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the previewer; keep it side-effect free.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
