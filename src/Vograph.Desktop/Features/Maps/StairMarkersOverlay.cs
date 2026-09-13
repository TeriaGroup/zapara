using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Vograph.Desktop.Features.Maps;

/// <summary>Fixed-size stair badges anchored to image coordinates outside the pan/zoom transform.</summary>
public sealed class StairMarkersOverlay : Control
{
    public static readonly StyledProperty<IReadOnlyList<StairMarker>> MarkersProperty =
        AvaloniaProperty.Register<StairMarkersOverlay, IReadOnlyList<StairMarker>>(nameof(Markers), []);
    public static readonly StyledProperty<PixelSize> ImageSizeProperty =
        AvaloniaProperty.Register<StairMarkersOverlay, PixelSize>(nameof(ImageSize));
    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<StairMarkersOverlay, double>(nameof(Scale), 1);
    public static readonly StyledProperty<double> OffsetXProperty =
        AvaloniaProperty.Register<StairMarkersOverlay, double>(nameof(OffsetX));
    public static readonly StyledProperty<double> OffsetYProperty =
        AvaloniaProperty.Register<StairMarkersOverlay, double>(nameof(OffsetY));

    static StairMarkersOverlay() => AffectsRender<StairMarkersOverlay>(
        MarkersProperty, ImageSizeProperty, ScaleProperty, OffsetXProperty, OffsetYProperty);

    public StairMarkersOverlay()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public IReadOnlyList<StairMarker> Markers { get => GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }
    public PixelSize ImageSize { get => GetValue(ImageSizeProperty); set => SetValue(ImageSizeProperty, value); }
    public double Scale { get => GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    public double OffsetX { get => GetValue(OffsetXProperty); set => SetValue(OffsetXProperty, value); }
    public double OffsetY { get => GetValue(OffsetYProperty); set => SetValue(OffsetYProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (ImageSize.Width <= 0 || ImageSize.Height <= 0) return;
        var ink = Brush("Brush.MapInk", Brushes.SteelBlue);
        var onInk = Brush("Brush.OnMapInk", Brushes.White);
        foreach (var marker in Markers)
        {
            var anchor = new Point(OffsetX + marker.X * ImageSize.Width * Scale,
                OffsetY + marker.Y * ImageSize.Height * Scale);
            if (!new Rect(Bounds.Size).Contains(anchor)) continue;
            var text = new FormattedText(marker.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Inter", weight: FontWeight.SemiBold), 14, onInk);
            var width = text.Width + 12;
            var height = text.Height + 6;
            var left = Math.Clamp(anchor.X + 7, 2, Math.Max(2, Bounds.Width - width - 2));
            var top = Math.Clamp(anchor.Y - height - 7, 2, Math.Max(2, Bounds.Height - height - 2));
            context.DrawLine(new Pen(ink, 2), anchor, new Point(left + 6, top + height));
            context.DrawEllipse(ink, new Pen(onInk, 2), anchor, 5, 5);
            context.DrawRectangle(ink, null, new Rect(left, top, width, height), 5, 5);
            context.DrawText(text, new Point(left + 6, top + 3));
        }
    }

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;
}
