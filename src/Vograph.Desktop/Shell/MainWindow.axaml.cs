using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>←/→/Home step the schedule day. A routed handler rather than a KeyBinding, so the focused
    /// element decides: caret keys inside a text field stay with it (e.Source), and a dialog keeps them too
    /// (HandleShortcut). It tunnels rather than bubbles because Avalonia's keyboard navigation, registered on
    /// the TopLevel before this window, already marks arrow keys Handled on their way up — bubbling would
    /// never see them, and handledEventsToo would step the day on top of whatever else consumed the key.</summary>
    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || e.Source is TextBox) return;
        if (DataContext is ShellViewModel vm && vm.HandleShortcut(e.Key)) e.Handled = true;
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
