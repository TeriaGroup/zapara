using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Controls;

/// <summary>
/// Pan/zoom viewport for one child (the map image with its highlight overlay). The child keeps its natural size;
/// a MatrixTransform (scale + translate, origin top-left) does the rest. Wheel zooms ×1.15 around the cursor,
/// left-drag pans, AutoFit fits the content the first time its size is known. With AnimateZoom the zoom steps
/// glide (spec §7, 160 ms); a drag never does.
/// </summary>
public sealed class ZoomPanel : Decorator
{
    public static readonly StyledProperty<double> ScaleProperty = AvaloniaProperty.Register<ZoomPanel, double>(nameof(Scale), 1.0);
    public static readonly StyledProperty<double> OffsetXProperty = AvaloniaProperty.Register<ZoomPanel, double>(nameof(OffsetX));
    public static readonly StyledProperty<double> OffsetYProperty = AvaloniaProperty.Register<ZoomPanel, double>(nameof(OffsetY));
    public static readonly StyledProperty<bool> AutoFitProperty = AvaloniaProperty.Register<ZoomPanel, bool>(nameof(AutoFit), true);
    public static readonly StyledProperty<bool> AnimateZoomProperty = AvaloniaProperty.Register<ZoomPanel, bool>(nameof(AnimateZoom));

    private const double WheelStep = 1.15;
    private const double ButtonStep = 1.25;

    /// <summary>One transform, mutated in place: a fresh instance per frame would make the RenderTransform of the
    /// child churn while a zoom transition samples it (T5 #3).</summary>
    private readonly MatrixTransform _transform = new();

    private bool _needsFit = true;
    private bool _batching;
    private Point? _dragLast;
    private Transitions? _zoomTransitions;

    public ZoomPanel()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public double Scale { get => GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    public double OffsetX { get => GetValue(OffsetXProperty); set => SetValue(OffsetXProperty, value); }
    public double OffsetY { get => GetValue(OffsetYProperty); set => SetValue(OffsetYProperty, value); }
    public bool AutoFit { get => GetValue(AutoFitProperty); set => SetValue(AutoFitProperty, value); }

    /// <summary>Bound to Motion.Enabled by the map views: «Анимации» off and every zoom step snaps.</summary>
    public bool AnimateZoom { get => GetValue(AnimateZoomProperty); set => SetValue(AnimateZoomProperty, value); }

    public event EventHandler? ViewChanged;

    private Size ContentSize => Child?.DesiredSize ?? default;

    /// <summary>Call after swapping the content (a new map): fit again on the next layout pass.</summary>
    public void RequestFit()
    {
        _needsFit = true;
        InvalidateArrange();
    }

    public void ZoomIn() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), ButtonStep);
    public void ZoomOut() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), 1 / ButtonStep);

    public void ZoomAt(Point viewportPoint, double factor)
    {
        var (s, ox, oy) = ZoomMath.ZoomAt(Scale, OffsetX, OffsetY, factor, viewportPoint.X, viewportPoint.Y);
        Apply(s, ox, oy, animate: true);
    }

    public void Fit()
    {
        var c = ContentSize;
        var (s, ox, oy) = ZoomMath.Fit(Bounds.Width, Bounds.Height, c.Width, c.Height);
        Apply(s, ox, oy, animate: true);
    }

    /// <summary>100 %, centered.</summary>
    public void ResetScale()
    {
        var c = ContentSize;
        var (ox, oy) = ZoomMath.Centered(Bounds.Width, Bounds.Height, c.Width, c.Height, 1);
        Apply(1, ox, oy, animate: true);
    }

    /// <summary>Spec §7 «Карта — DoubleTransition масштаба, 160 мс». Only zoom steps animate: a drag must follow the pointer at once.</summary>
    private Transitions ZoomTransitions() => _zoomTransitions ??= new Transitions
    {
        new DoubleTransition { Property = ScaleProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = MotionSettings.Ease },
        new DoubleTransition { Property = OffsetXProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = MotionSettings.Ease },
        new DoubleTransition { Property = OffsetYProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = MotionSettings.Ease },
    };

    private void Apply(double scale, double ox, double oy, bool animate)
    {
        Transitions = animate && AnimateZoom ? ZoomTransitions() : null;
        _batching = true;
        try
        {
            Scale = scale;
            OffsetX = ox;
            OffsetY = oy;
        }
        finally { _batching = false; } // a throwing setter must not leave direct-set reactions disabled (T5 #4)
        UpdateTransform();
        RaiseViewChanged();
    }

    private void RaiseViewChanged() => ViewChanged?.Invoke(this, EventArgs.Empty);

    private void UpdateTransform()
    {
        if (Child is null) return;
        Child.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        _transform.Matrix = new Matrix(Scale, 0, 0, Scale, OffsetX, OffsetY);
        if (!ReferenceEquals(Child.RenderTransform, _transform)) Child.RenderTransform = _transform; // one instance, mutated per frame (T5 #3)
        // Mutating Matrix in place changes no Avalonia property, so nothing marks the child dirty on its own —
        // and a zoom transition would then starve: no repaint, no compositor frame, no clock tick, no next value.
        Child.InvalidateVisual();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(Size.Infinity); // natural size: the transform, not layout, scales it
        var w = double.IsInfinity(availableSize.Width) ? ContentSize.Width : availableSize.Width;
        var h = double.IsInfinity(availableSize.Height) ? ContentSize.Height : availableSize.Height;
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is { } c)
        {
            c.Arrange(new Rect(c.DesiredSize));
            var didFit = false;
            if (_needsFit && AutoFit && c.DesiredSize.Width > 0 && finalSize.Width > 0)
            {
                _needsFit = false;
                var (s, ox, oy) = ZoomMath.Fit(finalSize.Width, finalSize.Height, c.DesiredSize.Width, c.DesiredSize.Height);
                Transitions = null; // an auto-fit lands at once: nothing glides towards a layout the user has not seen yet
                _batching = true;
                try
                {
                    Scale = s;
                    OffsetX = ox;
                    OffsetY = oy;
                }
                finally { _batching = false; }
                didFit = true;
            }
            UpdateTransform();
            if (didFit) RaiseViewChanged();
        }
        return finalSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChildProperty)
        {
            RequestFit();
            return;
        }
        if (_batching) return;
        if (change.Property != ScaleProperty && change.Property != OffsetXProperty && change.Property != OffsetYProperty) return;

        // A direct set (XAML attribute, style, animation, or a restored-state binding) bypasses Apply()/ArrangeOverride,
        // so react here too: clamp Scale back into range (guarding against re-entering this handler), then keep the
        // rendered transform and the ViewChanged contract in sync with it. The frames of a running zoom transition
        // arrive here as well, which is what keeps the matrix following it.
        var clamped = ZoomMath.Clamp(Scale);
        if (clamped != Scale)
        {
            _batching = true;
            try { Scale = clamped; }
            finally { _batching = false; }
        }
        UpdateTransform();
        RaiseViewChanged();
    }

    /// <summary>An otherwise empty Decorator is not hit-testable; painting the viewport makes wheel and drag land here.</summary>
    public override void Render(DrawingContext context) => context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0) return;
        ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? WheelStep : 1 / WheelStep);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragLast = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragLast is not { } last) return;
        var p = e.GetPosition(this);
        Apply(Scale, OffsetX + (p.X - last.X), OffsetY + (p.Y - last.Y), animate: false);
        _dragLast = p;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragLast is null) return;
        _dragLast = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragLast = null;
    }
}
