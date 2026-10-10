using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Shell;

public partial class StartupErrorWindow : Window
{
    public StartupErrorWindow() => InitializeComponent();

    /// <summary>Повторяет запуск: null — приложение запустилось (окно закрывается), иначе новая ошибка.</summary>
    public Func<int, StartupError?>? Retry { get; set; }
    public Func<string, Task>? CopyText { get; set; }

    private async void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StartupError error) return;
        try
        {
            Directory.CreateDirectory(error.DataDir);
            await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(error.DataDir));
        }
        catch (Exception)
        {
            // Nothing else to fall back to on this window: the path is on screen for the user to copy.
        }
    }

    public void OnRetry(object? sender, RoutedEventArgs e)
    {
        if (Retry is null || DataContext is not StartupError current) return;
        var next = Retry(current.Attempt + 1);
        if (next is null) { Close(); return; }
        DataContext = next with { Attempt = current.Attempt + 1 };
    }

    public async void OnCopyReport(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StartupError error) return;
        var report = StartupErrorCatalog.Report(error, AppVersion.Short, DateTimeOffset.Now);
        try
        {
            if (CopyText is not null) await CopyText(report);
            else if (Clipboard is { } clipboard) await clipboard.SetTextAsync(report);
            else return;
            if (this.FindControl<TextBlock>("CopyStatus") is { } status) status.IsVisible = true;
        }
        catch (Exception)
        {
            // The same text is in the log file shown under «Подробности».
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
