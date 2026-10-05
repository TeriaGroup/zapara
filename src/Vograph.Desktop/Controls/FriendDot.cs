using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Vograph.Desktop.Controls;

public enum DotFill { Off, Ring, Half, ThreeQuarters, Full }

/// <summary>
/// Friend traffic light: color = whose group (Brush.Friend1..5), fill = how close (IntersectionService score).
/// Drawn directly so the partial fills stay crisp at 9px.
/// </summary>
public class FriendDot : Control
{
    public static readonly StyledProperty<int> ColorIndexProperty = AvaloniaProperty.Register<FriendDot, int>(nameof(ColorIndex));
    public static readonly StyledProperty<DotFill> FillProperty = AvaloniaProperty.Register<FriendDot, DotFill>(nameof(Fill), DotFill.Ring);

    static FriendDot() => AffectsRender<FriendDot>(ColorIndexProperty, FillProperty);

    public FriendDot() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public int ColorIndex { get => GetValue(ColorIndexProperty); set => SetValue(ColorIndexProperty, value); }
    public DotFill Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    /// <summary>100 same room → Full, 75 floor → ThreeQuarters, 50 building → Half, 25 university → Ring, else Off.</summary>
    public static DotFill FromScore(int score) => score switch
    {
        >= 100 => DotFill.Full,
        >= 75 => DotFill.ThreeQuarters,
        >= 50 => DotFill.Half,
        >= 25 => DotFill.Ring,
        _ => DotFill.Off
    };

    protected override Size MeasureOverride(Size availableSize) => new(9, 9);

    private IBrush? Resolve(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : null;

    public override void Render(DrawingContext context)
    {
        var brush = Fill == DotFill.Off
            ? Resolve("Brush.LineStrong")
            : Resolve($"Brush.Friend{Math.Clamp(ColorIndex, 0, 4) + 1}");
        if (brush is null) return;

        var r = Math.Min(Bounds.Width, Bounds.Height) / 2;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);

        if (Fill == DotFill.Full)
        {
            context.DrawEllipse(brush, null, c, r, r);
            return;
        }

        context.DrawEllipse(null, new Pen(brush, 2), c, r - 1, r - 1);
        if (Fill is DotFill.Off or DotFill.Ring) return;

        var sweep = Fill == DotFill.Half ? 180.0 : 270.0;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(c, isFilled: true);
            g.LineTo(OnCircle(c, r, -90));
            g.ArcTo(OnCircle(c, r, -90 + sweep), new Size(r, r), 0, isLargeArc: sweep > 180, SweepDirection.Clockwise);
            g.EndFigure(isClosed: true);
        }
        context.DrawGeometry(brush, null, geometry);
    }

    private static Point OnCircle(Point c, double r, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }
}
