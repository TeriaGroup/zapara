using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Shell;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private UiPrefs? Prefs => (DataContext as ShellViewModel)?.App.Prefs;

    private void OnOpened(object? sender, EventArgs e)
    {
        var prefs = Prefs;
        if (prefs?.Window is null) return;
        var restored = WindowBoundsLogic.Restore(prefs.Window, Screens.All.Select(s => s.Bounds).ToList(), new PixelSize((int)MinWidth, (int)MinHeight));
        if (restored is null) return;
        Position = new PixelPoint(restored.X, restored.Y);
        Width = restored.Width;
        Height = restored.Height;
        if (restored.Maximized) WindowState = WindowState.Maximized;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var prefs = Prefs;
        if (prefs is null) return;
        var maximized = WindowState == WindowState.Maximized;
        // While maximized Position/Width describe the maximized frame; keep the last normal bounds instead.
        prefs.Window = maximized && prefs.Window is not null
            ? prefs.Window with { Maximized = true }
            : new WindowBounds(Position.X, Position.Y, (int)Width, (int)Height, maximized);
        prefs.Save();
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
