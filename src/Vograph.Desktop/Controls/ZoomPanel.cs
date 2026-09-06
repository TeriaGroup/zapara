using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Vograph.Desktop.Controls;

/// <summary>
/// Pan/zoom viewport for one child (the map image with its highlight overlay). The child keeps its natural size;
/// a MatrixTransform (scale + translate, origin top-left) does the rest. Wheel zooms ×1.15 around the cursor,
/// left-drag pans, AutoFit fits the content the first time its size is known.
/// </summary>
public sealed class ZoomPanel : Decorator
{
    public static readonly StyledProperty<double> ScaleProperty = AvaloniaProperty.Register<ZoomPanel, double>(nameof(Scale), 1.0);
    public static readonly StyledProperty<double> OffsetXProperty = AvaloniaProperty.Register<ZoomPanel, double>(nameof(OffsetX));
    public static readonly StyledProperty<double> OffsetYProperty = AvaloniaProperty.Register<ZoomPanel, double>(nameof(OffsetY));
    public static readonly StyledProperty<bool> AutoFitProperty = AvaloniaProperty.Register<ZoomPanel, bool>(nameof(AutoFit), true);

    private const double WheelStep = 1.15;
    private const double ButtonStep = 1.25;
    private bool _needsFit = true;
    private Point? _dragLast;

    public ZoomPanel()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public double Scale { get => GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    public double OffsetX { get => GetValue(OffsetXProperty); set => SetValue(OffsetXProperty, value); }
    public double OffsetY { get => GetValue(OffsetYProperty); set => SetValue(OffsetYProperty, value); }
    public bool AutoFit { get => GetValue(AutoFitProperty); set => SetValue(AutoFitProperty, value); }

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
        Apply(s, ox, oy);
    }

    public void Fit()
    {
        var c = ContentSize;
        var (s, ox, oy) = ZoomMath.Fit(Bounds.Width, Bounds.Height, c.Width, c.Height);
        Apply(s, ox, oy);
    }

    /// <summary>100 %, centered.</summary>
    public void ResetScale()
    {
        var c = ContentSize;
        var (ox, oy) = ZoomMath.Centered(Bounds.Width, Bounds.Height, c.Width, c.Height, 1);
        Apply(1, ox, oy);
    }

    private void Apply(double scale, double ox, double oy)
    {
        Scale = scale;
        OffsetX = ox;
        OffsetY = oy;
        UpdateTransform();
        RaiseViewChanged();
    }

    private void RaiseViewChanged() => ViewChanged?.Invoke(this, EventArgs.Empty);

    private void UpdateTransform()
    {
        if (Child is null) return;
        Child.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        Child.RenderTransform = new MatrixTransform(new Matrix(Scale, 0, 0, Scale, OffsetX, OffsetY));
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
                Scale = s;
                OffsetX = ox;
                OffsetY = oy;
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
        if (change.Property == ChildProperty) RequestFit();
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
        Apply(Scale, OffsetX + (p.X - last.X), OffsetY + (p.Y - last.Y));
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
