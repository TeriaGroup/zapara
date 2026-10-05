using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Vograph.Desktop.Controls;

/// <summary>One-finger horizontal navigation, scoped to planner content.</summary>
public sealed class PlannerTouchNavigation
{
    private readonly Control _owner;
    private readonly Func<bool> _enabled;
    private readonly Func<object?> _key;
    private readonly Action<int> _navigate;
    private readonly HashSet<IPointer> _touches = new();
    private TopLevel? _root;
    private IPointer? _pointer;
    private Point _start;
    private object? _startKey;
    private bool _claimed;

    public PlannerTouchNavigation(Control owner, Func<bool> enabled, Func<object?> key, Action<int> navigate)
    {
        _owner = owner;
        _enabled = enabled;
        _key = key;
        _navigate = navigate;
        owner.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        owner.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel, handledEventsToo: true);
        owner.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel, handledEventsToo: true);
        owner.AddHandler(InputElement.PointerCaptureLostEvent, CaptureLost, RoutingStrategies.Direct, handledEventsToo: true);
        owner.AttachedToVisualTree += (_, _) => AttachRoot();
        owner.DetachedFromVisualTree += (_, _) => { DetachRoot(); Reset(); };
        AttachRoot();
    }

    private void AttachRoot()
    {
        DetachRoot();
        _root = TopLevel.GetTopLevel(_owner);
        if (_root is null) return;
        _root.AddHandler(InputElement.PointerPressedEvent, ObservePressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _root.AddHandler(InputElement.PointerReleasedEvent, ObserveReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        if (_root is Window window) window.Deactivated += WindowDeactivated;
    }

    private void DetachRoot()
    {
        if (_root is null) return;
        _root.RemoveHandler(InputElement.PointerPressedEvent, ObservePressed);
        _root.RemoveHandler(InputElement.PointerReleasedEvent, ObserveReleased);
        if (_root is Window window) window.Deactivated -= WindowDeactivated;
        _root = null;
    }

    private void WindowDeactivated(object? sender, EventArgs e) => Reset();

    private void ObservePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch) return;
        // Scroll recognizers and cancelled touches can finish without a routed release.
        // Avalonia marks a new touch primary only when the previous contacts have ended.
        if (e.Pointer.IsPrimary && !_touches.Contains(e.Pointer)) Reset();
        _touches.Add(e.Pointer);
        if (_touches.Count > 1) Cancel();
    }

    private void ObserveReleased(object? sender, PointerReleasedEventArgs e)
    {
        _touches.Remove(e.Pointer);
        // Also clear candidates released outside the content or captured by its ScrollViewer.
        if (e.Pointer == _pointer) Cancel();
    }

    public void Reset()
    {
        Cancel();
        _touches.Clear();
    }

    private void Cancel()
    {
        var pointer = _pointer;
        _pointer = null;
        _startKey = null;
        _claimed = false;
        if (pointer?.Captured == _owner) pointer.Capture(null);
    }

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch) return;
        _touches.Add(e.Pointer);
        if (_touches.Count != 1) { Cancel(); return; }
        var position = e.GetPosition(_owner);
        if (!_enabled() || !PlannerSwipePolicy.CanStart(position.X, _owner.Bounds.Width) || IsInteractive(e.Source as Visual)) return;
        _pointer = e.Pointer;
        _start = position;
        _startKey = _key();
        _claimed = false;
    }

    private bool IsInteractive(Visual? source)
    {
        for (var current = source; current is not null && current != _owner; current = current.GetVisualParent())
        {
            if (current is Button button && !button.Classes.Contains("weekday")) return true;
            if (current is TextBox or RangeBase or ComboBox or CalendarDatePicker or Switch or SegmentedControl) return true;
        }
        return false;
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer != _pointer) return;
        if (!_enabled() || !Equals(_startKey, _key()) || _touches.Count != 1) { Cancel(); return; }
        var delta = e.GetPosition(_owner) - _start;
        if (!_claimed)
        {
            if (PlannerSwipePolicy.IsVertical(delta.X, delta.Y)) { Cancel(); return; }
            if (!PlannerSwipePolicy.IsHorizontal(delta.X, delta.Y)) return;
            _claimed = true;
            e.Pointer.Capture(_owner);
        }
        e.Handled = true;
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        _touches.Remove(e.Pointer);
        if (e.Pointer != _pointer) return;
        var delta = e.GetPosition(_owner) - _start;
        var direction = _claimed && _enabled() && Equals(_startKey, _key())
            ? PlannerSwipePolicy.Direction(delta.X, delta.Y) : 0;
        var claimed = _claimed;
        Cancel();
        if (claimed) e.Handled = true;
        if (direction != 0) _navigate(direction);
    }

    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (e.Pointer == _pointer && e.Pointer.Captured != _owner) Cancel();
    }
}
