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
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>←/→/Home step the schedule day, Escape closes the dialog or the fullscreen map. A bubbling handler
    /// rather than a KeyBinding, so the focused element gets first refusal: a child that already consumed the key
    /// (TextBox caret keys, ListBox/Slider/ComboBox arrows) keeps it, because the first line bails on an event some
    /// descendant marked Handled. The one Handled event we still act on is the one raised on the window itself: with
    /// nothing focused Avalonia's TopLevel keyboard-navigation handler — registered before this window's — marks arrow
    /// keys Handled while looking for a focus target, and a plain Bubble registration would never be called at all.
    /// Hence handledEventsToo plus the Source check rather than plain Bubble (which loses the shortcut on a
    /// freshly opened window) or Tunnel (which would steal the keys from every child before it can react).
    /// Text fields are excluded by Source — except for Escape, which must still close a dialog whose search box has
    /// focus — and dialogs by HandleShortcut's own Dialogs.HasDialog guard.</summary>
    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled && !ReferenceEquals(e.Source, this)) return; // a focused child answered first
        if (e.KeyModifiers != KeyModifiers.None || (e.Key != Key.Escape && e.Source is TextBox)) return;
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
