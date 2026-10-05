using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Dialogs;

public partial class DialogHostView : UserControl
{
    private DialogHostViewModel? _vm;

    /// <summary>Bumped by every open and close so a run that has been taken over neither hides the host nor
    /// takes the card's transform away from the newer one.</summary>
    private int _generation;

    public DialogHostView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as DialogHostViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DialogHostViewModel.IsOpen) || _vm is null) return;
        if (_vm.IsOpen) _ = OpenAsync(_vm.Motion.Duration(180));
        else _ = CloseAsync(_vm.Motion.Duration(120));
    }

    /// <summary>Spec §6: fade over 180 ms, the backdrop fading with it. No scale — a 0.96→1 transform re-rasters
    /// Inter every frame and reads as shaking text. Focus moves into the host so Enter/Escape work at once
    /// (deferred so a dialog view's own OnLoaded focus — the search box — wins).</summary>
    private async Task OpenAsync(TimeSpan duration)
    {
        var generation = ++_generation;
        Root.IsVisible = true;
        // A previous close ended at Opacity 0, and FillMode.Forward writes that at LOCAL priority: hand the
        // property back to its natural value, or a snap-open («Анимации» off) would show nothing at all.
        Backdrop.ClearValue(OpacityProperty);
        Card.ClearValue(OpacityProperty);
        Dispatcher.UIThread.Post(() => { if (!Root.IsKeyboardFocusWithin) Root.Focus(); });
        if (duration == TimeSpan.Zero) return;
        try
        {
            await Task.WhenAll(
                Fade(0, 1, duration).RunAsync(Backdrop),
                Fade(0, 1, duration).RunAsync(Card));
        }
        catch (Exception ex)
        {
            Warn(ex); // a host torn down mid-animation is not an error, but it is not silent either
        }
        finally
        {
            // The transform animator writes RenderTransform at local priority and no FillMode ever releases it
            // (T9-R4): give it back to the styles, unless a newer open already owns the card.
            if (generation == _generation) Card.ClearValue(RenderTransformProperty);
        }
    }

    /// <summary>120 ms fade-out of card and backdrop; the host hides afterwards unless a new dialog opened meanwhile.</summary>
    private async Task CloseAsync(TimeSpan duration)
    {
        var generation = ++_generation;
        if (duration > TimeSpan.Zero)
        {
            try { await Task.WhenAll(Fade(1, 0, duration).RunAsync(Backdrop), Fade(1, 0, duration).RunAsync(Card)); }
            catch (Exception ex) { Warn(ex); }
        }
        if (generation == _generation) Root.IsVisible = false;
    }

    private static Animation Fade(double from, double to, TimeSpan duration) => new()
    {
        Duration = duration, Easing = MotionSettings.Ease, FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, from) } },
            new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, to) } },
        }
    };

    /// <summary>The trace listeners rather than AppLog: this view only ever sees a DialogHostViewModel, which
    /// carries the motion settings but no log — the same sink MotionSettings falls back to.</summary>
    private static void Warn(Exception ex) => Trace.TraceWarning($"dialog motion: {ex.GetType().Name}: {ex.Message}");

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is DialogHostViewModel vm) vm.DismissCommand.Execute(null);
    }

    /// <summary>
    /// Enter-to-confirm as a bubbling KeyDown instead of a Window.KeyBinding: a KeyBinding gesture is matched
    /// even when a descendant (a multi-line TextBox inserting a newline) already set e.Handled, which would
    /// wrongly confirm the dialog while the user is still typing. A plain bubbling handler skips once handled.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not DialogHostViewModel vm) return;
        vm.ConfirmCurrentCommand.Execute(null);
        e.Handled = true;
    }
}
