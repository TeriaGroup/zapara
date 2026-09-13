using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Core.Campus;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Maps;

/// <summary>Route overlay: ghost, draw-on, marching dash, start/end, traveler. Motion off snaps to the full path.</summary>
public sealed class RoutePathLayer : Control
{
    public static readonly StyledProperty<IReadOnlyList<IReadOnlyList<Point>>> StrokesProperty =
        AvaloniaProperty.Register<RoutePathLayer, IReadOnlyList<IReadOnlyList<Point>>>(nameof(Strokes), []);
    public static readonly StyledProperty<double> DrawProgressProperty =
        AvaloniaProperty.Register<RoutePathLayer, double>(nameof(DrawProgress), 1);
    public static readonly StyledProperty<double> MarchProperty =
        AvaloniaProperty.Register<RoutePathLayer, double>(nameof(March));

    private MotionSettings? _motion;
    private CancellationTokenSource? _run;

    static RoutePathLayer()
    {
        AffectsRender<RoutePathLayer>(StrokesProperty, DrawProgressProperty, MarchProperty);
        StrokesProperty.Changed.AddClassHandler<RoutePathLayer>((c, _) => c.Restart());
    }

    public RoutePathLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Focusable = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public IReadOnlyList<IReadOnlyList<Point>> Strokes
    {
        get => GetValue(StrokesProperty);
        set => SetValue(StrokesProperty, value);
    }

    public double DrawProgress
    {
        get => GetValue(DrawProgressProperty);
        set => SetValue(DrawProgressProperty, value);
    }

    public double March
    {
        get => GetValue(MarchProperty);
        set => SetValue(MarchProperty, value);
    }

    public PathTraceFrame Frame(double? progress = null)
    {
        var trace = ToTrace(Strokes);
        var motion = MotionSettings.Resolve(this);
        var t = progress ?? (motion.Enabled ? DrawProgress : 1);
        return PathTrace.At(trace, t);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Resolve();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Listen(null);
        Stop();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Resolve();
    }

    public override void Render(DrawingContext context)
    {
        var trace = ToTrace(Strokes);
        if (trace.Count == 0) return;
        var motion = MotionSettings.Resolve(this);
        var progress = motion.Enabled ? MotionSettings.Ease.Ease(DrawProgress) : 1;
        var frame = PathTrace.At(trace, progress);
        var ink = Brush("Brush.MapInk", Brushes.SteelBlue);
        var soft = Brush("Brush.MapInkSoft", Brushes.LightSteelBlue);
        var onInk = Brush("Brush.OnMapInk", Brushes.White);
        var ghost = new Pen(soft, 5) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var solid = new Pen(ink, 4) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        foreach (var stroke in trace) DrawLine(context, ghost, stroke);
        foreach (var stroke in frame.Revealed) DrawLine(context, solid, stroke);
        if (frame.Complete && motion.Enabled)
        {
            var dash = new Pen(onInk, 2)
            {
                LineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
                DashStyle = new DashStyle([10, 8], PathTrace.DashOffset(March * PathTrace.LoopMs, PathTrace.LoopMs, PathTrace.DashPeriod)),
            };
            foreach (var stroke in trace) DrawLine(context, dash, stroke);
        }
        if (frame.Start is { } start)
            context.DrawEllipse(null, new Pen(ink, 2), Pt(start), 5, 5);
        if (frame.End is { } dest)
        {
            context.DrawEllipse(ink, new Pen(onInk, 2), Pt(dest), 6, 6);
        }
        var head = motion.Enabled
            ? (frame.Complete ? PathTrace.At(trace, March).Head : frame.Head)
            : null;
        if (head is { } h)
        {
            context.DrawEllipse(onInk, new Pen(ink, 2), Pt(h), 5, 5);
        }
    }

    private void Resolve()
    {
        Listen(this.IsAttachedToVisualTree() ? MotionSettings.Resolve(this) : null);
        Restart();
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
        if (e.PropertyName is null or nameof(MotionSettings.Enabled)) Restart();
    }

    private void Restart()
    {
        Stop();
        if (ToTrace(Strokes).Count == 0)
        {
            DrawProgress = 1;
            March = 0;
            return;
        }
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
        Run(drawFor, _motion.Duration(PathTrace.LoopMs), cts);
    }

    private void Stop()
    {
        var cts = _run;
        _run = null;
        cts?.Cancel();
        cts?.Dispose();
    }

    private async void Run(TimeSpan drawFor, TimeSpan loopFor, CancellationTokenSource cts)
    {
        try
        {
            await Animate(DrawProgressProperty, 0, 1, drawFor, ease: true).RunAsync(this, cts.Token);
            if (cts.IsCancellationRequested) return;
            DrawProgress = 1;
            if (loopFor <= TimeSpan.Zero) return;
            while (!cts.IsCancellationRequested)
                await Animate(MarchProperty, 0, 1, loopFor, ease: false).RunAsync(this, cts.Token);
        }
        catch (Exception)
        {
            // cancelled or detached: the replacement Restart owns the next loop
        }
        finally
        {
            if (ReferenceEquals(_run, cts)) _run = null;
            cts.Dispose();
        }
    }

    private static Animation Animate(AvaloniaProperty property, double from, double to, TimeSpan duration, bool ease) => new()
    {
        Duration = duration,
        Easing = ease ? MotionSettings.Ease : new LinearEasing(),
        Children =
        {
            new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(property, from) } },
            new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(property, to) } },
        }
    };

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;

    private static void DrawLine(DrawingContext context, Pen pen, IReadOnlyList<TracePoint> pts)
    {
        if (pts.Count < 2) return;
        context.DrawGeometry(null, pen, Open(pts));
    }

    private static StreamGeometry Open(IReadOnlyList<TracePoint> pts)
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

    internal static IReadOnlyList<IReadOnlyList<TracePoint>> ToTrace(IReadOnlyList<IReadOnlyList<Point>>? strokes)
    {
        if (strokes is null || strokes.Count == 0) return [];
        List<IReadOnlyList<TracePoint>> list = [];
        foreach (var stroke in strokes)
        {
            if (stroke.Count < 2) continue;
            var pts = new TracePoint[stroke.Count];
            for (var i = 0; i < stroke.Count; i++)
                pts[i] = new TracePoint(stroke[i].X, stroke[i].Y);
            list.Add(pts);
        }
        return list;
    }

    private static Point Pt(TracePoint p) => new(p.X, p.Y);
}
