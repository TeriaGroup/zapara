using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Core.Campus;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Maps;

/// <summary>Schematic stacked floors on a 2D canvas. Orbit is drag-only and gated by AllowOrbit.</summary>
public sealed class CampusStackView : Control
{
    public static readonly StyledProperty<Route?> RouteProperty =
        AvaloniaProperty.Register<CampusStackView, Route?>(nameof(Route));
    public static readonly StyledProperty<string> BuildingProperty =
        AvaloniaProperty.Register<CampusStackView, string>(nameof(Building), "ГК");
    public static readonly StyledProperty<IReadOnlyDictionary<int, Bitmap>?> FloorImagesProperty =
        AvaloniaProperty.Register<CampusStackView, IReadOnlyDictionary<int, Bitmap>?>(nameof(FloorImages));
    public static readonly StyledProperty<bool> AllowOrbitProperty =
        AvaloniaProperty.Register<CampusStackView, bool>(nameof(AllowOrbit));
    public static readonly StyledProperty<double> DrawProgressProperty =
        AvaloniaProperty.Register<CampusStackView, double>(nameof(DrawProgress), 1);
    public static readonly StyledProperty<double> MarchProperty =
        AvaloniaProperty.Register<CampusStackView, double>(nameof(March));

    private double _yaw = CampusStackProjector.DefaultYaw;
    private double _pitch = CampusStackProjector.DefaultPitch;
    private Point? _drag;
    private MotionSettings? _motion;
    private CancellationTokenSource? _run;

    static CampusStackView()
    {
        AffectsRender<CampusStackView>(RouteProperty, BuildingProperty, FloorImagesProperty, AllowOrbitProperty, DrawProgressProperty, MarchProperty);
        RouteProperty.Changed.AddClassHandler<CampusStackView>((c, _) => c.RestartTrace());
        BuildingProperty.Changed.AddClassHandler<CampusStackView>((c, _) => c.RestartTrace());
    }

    public CampusStackView()
    {
        ClipToBounds = true;
        Focusable = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public Route? Route { get => GetValue(RouteProperty); set => SetValue(RouteProperty, value); }
    public string Building { get => GetValue(BuildingProperty); set => SetValue(BuildingProperty, value); }
    public IReadOnlyDictionary<int, Bitmap>? FloorImages { get => GetValue(FloorImagesProperty); set => SetValue(FloorImagesProperty, value); }
    public bool AllowOrbit { get => GetValue(AllowOrbitProperty); set => SetValue(AllowOrbitProperty, value); }
    public double DrawProgress { get => GetValue(DrawProgressProperty); set => SetValue(DrawProgressProperty, value); }
    public double March { get => GetValue(MarchProperty); set => SetValue(MarchProperty, value); }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width < 2 || size.Height < 2) return;
        var building = string.IsNullOrEmpty(Building) ? "ГК" : Building;
        var scene = CampusStackProjector.Project(Route, building, size.Width, size.Height, _yaw, _pitch);
        var card = Brush("Brush.Card") ?? Brushes.Gray;
        var line = Brush("Brush.LineStrong") ?? Brushes.DimGray;
        var ink = Brush("Brush.MapInk") ?? Brushes.SteelBlue;
        var floorPen = new Pen(line, 1);
        var pathPen = new Pen(ink, 3) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var ghostPen = new Pen(Brush("Brush.MapInkSoft") ?? Brushes.LightSteelBlue, 4)
        { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var onInk = Brush("Brush.OnMapInk") ?? Brushes.White;
        var motion = MotionSettings.Resolve(this);
        var strokes = TraceOf(scene);
        var progress = motion.Enabled ? MotionSettings.Ease.Ease(DrawProgress) : 1;
        var frame = PathTrace.At(strokes, progress);
        Pen? dash = frame.Complete && motion.Enabled
            ? new Pen(onInk, 1.5)
            {
                LineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
                DashStyle = new DashStyle([8, 6], PathTrace.DashOffset(March * PathTrace.LoopMs, PathTrace.LoopMs, PathTrace.DashPeriod)),
            }
            : null;

        foreach (var item in CampusStackProjector.PaintSequence(scene))
        {
            if (item.Kind == "floor" && item.Quad is { } quad)
            {
                if (quad.Points2.Count < 4) continue;
                var geo = Closed(quad.Points2);
                if (FloorImages is { } images && images.TryGetValue(quad.Floor, out var bmp))
                    DrawTextured(context, bmp, quad.Points2);
                else
                    context.DrawGeometry(card, floorPen, geo);
                context.DrawGeometry(null, floorPen, geo);
                continue;
            }
            if (item.Line is not { Points2.Count: >= 2 } poly) continue;
            var stroke = ToTrace(poly);
            context.DrawGeometry(null, ghostPen, OpenTrace(stroke));
            if (Revealed(frame, stroke))
                context.DrawGeometry(null, pathPen, OpenTrace(stroke));
            if (dash is not null)
                context.DrawGeometry(null, dash, OpenTrace(stroke));
        }
        if (frame.Start is { } start)
            context.DrawEllipse(null, new Pen(ink, 2), Pt(start), 4, 4);
        if (frame.End is { } dest)
            context.DrawEllipse(ink, new Pen(onInk, 2), Pt(dest), 5, 5);
        var head = motion.Enabled ? (frame.Complete ? PathTrace.At(strokes, March).Head : frame.Head) : null;
        if (head is { } h)
            context.DrawEllipse(onInk, new Pen(ink, 2), Pt(h), 4, 4);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ResolveTrace();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Listen(null);
        StopTrace();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ResolveTrace();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!AllowOrbit) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _drag = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!AllowOrbit || _drag is not { } last) return;
        var p = e.GetPosition(this);
        _yaw += (p.X - last.X) * 0.01;
        _pitch = Math.Clamp(_pitch - (p.Y - last.Y) * 0.01, 0.08, 1.2);
        _drag = p;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = null;
        if (e.Pointer.Captured == this) e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _drag = null;
    }

    private IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : null;

    private static StreamGeometry Closed(IReadOnlyList<StackPoint2> pts)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(Pt(pts[0]), isFilled: true);
            for (var i = 1; i < pts.Count; i++) ctx.LineTo(Pt(pts[i]));
            ctx.EndFigure(isClosed: true);
        }
        return g;
    }

    private static StreamGeometry OpenTrace(IReadOnlyList<TracePoint> pts)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(Pt(pts[0]), isFilled: false);
            for (var i = 1; i < pts.Count; i++) ctx.LineTo(Pt(pts[i]));
            ctx.EndFigure(isClosed: false);
        }
        return g;
    }

    private static TracePoint[] ToTrace(StackPolyline line)
    {
        var pts = new TracePoint[line.Points2.Count];
        for (var i = 0; i < line.Points2.Count; i++)
            pts[i] = new TracePoint(line.Points2[i].X, line.Points2[i].Y);
        return pts;
    }

    private static bool Revealed(PathTraceFrame frame, TracePoint[] stroke)
    {
        foreach (var revealed in frame.Revealed)
        {
            if (revealed.Count == 0 || stroke.Length == 0) continue;
            if (revealed[0] == stroke[0] && revealed[^1] == stroke[^1]) return true;
        }
        return false;
    }

    private static IReadOnlyList<IReadOnlyList<TracePoint>> TraceOf(StackScene scene)
    {
        List<IReadOnlyList<TracePoint>> strokes = [];
        foreach (var line in scene.Polylines)
        {
            if (line.Points2.Count < 2) continue;
            var pts = new TracePoint[line.Points2.Count];
            for (var i = 0; i < line.Points2.Count; i++)
                pts[i] = new TracePoint(line.Points2[i].X, line.Points2[i].Y);
            strokes.Add(pts);
        }
        return strokes;
    }

    private void ResolveTrace()
    {
        Listen(this.IsAttachedToVisualTree() ? MotionSettings.Resolve(this) : null);
        RestartTrace();
    }

    private void Listen(MotionSettings? motion)
    {
        if (ReferenceEquals(motion, MotionSettings.Off)) motion = null;
        if (ReferenceEquals(_motion, motion)) return;
        if (_motion is not null) _motion.PropertyChanged -= OnMotionChanged;
        _motion = motion;
        if (_motion is not null) _motion.PropertyChanged += OnMotionChanged;
    }

    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(MotionSettings.Enabled)) RestartTrace();
    }

    private void RestartTrace()
    {
        StopTrace();
        if (_motion is not { Enabled: true })
        {
            DrawProgress = 1;
            March = 0;
            InvalidateVisual();
            return;
        }
        DrawProgress = 0;
        March = 0;
        var drawFor = _motion.Duration(PathTrace.DrawMs);
        if (drawFor <= TimeSpan.Zero)
        {
            DrawProgress = 1;
            return;
        }
        var cts = new CancellationTokenSource();
        _run = cts;
        RunTrace(drawFor, _motion.Duration(PathTrace.LoopMs), cts);
    }

    private void StopTrace()
    {
        var cts = _run;
        _run = null;
        cts?.Cancel();
        cts?.Dispose();
    }

    private async void RunTrace(TimeSpan drawFor, TimeSpan loopFor, CancellationTokenSource cts)
    {
        try
        {
            await TraceAnim(DrawProgressProperty, 0, 1, drawFor, true).RunAsync(this, cts.Token);
            if (cts.IsCancellationRequested) return;
            DrawProgress = 1;
            if (loopFor <= TimeSpan.Zero) return;
            while (!cts.IsCancellationRequested)
                await TraceAnim(MarchProperty, 0, 1, loopFor, false).RunAsync(this, cts.Token);
        }
        catch (Exception)
        {
            // cancelled or detached
        }
        finally
        {
            if (ReferenceEquals(_run, cts)) _run = null;
            cts.Dispose();
        }
    }

    private static Animation TraceAnim(AvaloniaProperty property, double from, double to, TimeSpan duration, bool ease) => new()
    {
        Duration = duration,
        Easing = ease ? MotionSettings.Ease : new LinearEasing(),
        Children =
        {
            new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(property, from) } },
            new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(property, to) } },
        }
    };

    private static void DrawTextured(DrawingContext context, Bitmap bmp, IReadOnlyList<StackPoint2> pts)
    {
        var p00 = pts[0];
        var p10 = pts[1];
        var p01 = pts[3];
        var w = bmp.PixelSize.Width;
        var h = bmp.PixelSize.Height;
        if (w <= 0 || h <= 0) return;
        var m = new Matrix(
            (p10.X - p00.X) / w, (p10.Y - p00.Y) / w,
            (p01.X - p00.X) / h, (p01.Y - p00.Y) / h,
            p00.X, p00.Y);
        using (context.PushTransform(m))
            context.DrawImage(bmp, new Rect(0, 0, w, h));
    }

    private static Point Pt(StackPoint2 p) => new(p.X, p.Y);
    private static Point Pt(TracePoint p) => new(p.X, p.Y);
}
