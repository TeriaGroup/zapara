using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Vograph.Desktop.Shell;

public partial class StartupErrorWindow : Window
{
    public StartupErrorWindow() => InitializeComponent();

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

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
