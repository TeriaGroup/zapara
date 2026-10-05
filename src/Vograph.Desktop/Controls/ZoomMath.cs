namespace Vograph.Desktop.Controls;

/// <summary>Pure zoom/pan arithmetic for ZoomPanel. Offsets are the viewport position of the content's top-left corner.</summary>
public static class ZoomMath
{
    public const double Min = 0.25;
    public const double Max = 6.0;

    public static double Clamp(double scale) => Math.Clamp(scale, Min, Max);

    /// <summary>Scale by <paramref name="factor"/> so that the content point under the anchor (viewport coordinates) stays put.</summary>
    public static (double Scale, double OffsetX, double OffsetY) ZoomAt(double scale, double ox, double oy, double factor, double anchorX, double anchorY)
    {
        var next = Clamp(scale * factor);
        var k = next / scale;
        return (next, anchorX - (anchorX - ox) * k, anchorY - (anchorY - oy) * k);
    }

    /// <summary>Largest scale showing the whole content, centered. Identity when either size is unknown.</summary>
    public static (double Scale, double OffsetX, double OffsetY) Fit(double viewW, double viewH, double contentW, double contentH)
    {
        if (viewW <= 0 || viewH <= 0 || contentW <= 0 || contentH <= 0) return (1, 0, 0);
        var s = Clamp(Math.Min(viewW / contentW, viewH / contentH));
        var (ox, oy) = Centered(viewW, viewH, contentW, contentH, s);
        return (s, ox, oy);
    }

    public static (double OffsetX, double OffsetY) Centered(double viewW, double viewH, double contentW, double contentH, double scale) =>
        ((viewW - contentW * scale) / 2, (viewH - contentH * scale) / 2);
}
