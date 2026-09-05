using Avalonia;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Shell;

public static class WindowBoundsLogic
{
    /// <summary>
    /// Bounds to apply on startup, or null to fall back to the default centered window.
    /// Rejects windows that would be (almost) invisible, e.g. saved on a monitor that is gone.
    /// </summary>
    public static WindowBounds? Restore(WindowBounds? saved, IReadOnlyList<PixelRect> screens, PixelSize minSize)
    {
        if (saved is null || screens.Count == 0) return null;
        var width = Math.Max(saved.Width, minSize.Width);
        var height = Math.Max(saved.Height, minSize.Height);
        var rect = new PixelRect(saved.X, saved.Y, width, height);
        var windowArea = (double)width * height;
        var visible = screens.Any(s =>
        {
            if (!s.Intersects(rect)) return false;
            var i = s.Intersect(rect);
            return (double)i.Width * i.Height / windowArea >= 0.25;
        });
        return visible ? saved with { Width = width, Height = height } : null;
    }
}
