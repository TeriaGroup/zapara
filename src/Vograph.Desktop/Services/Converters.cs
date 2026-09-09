using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace Vograph.Desktop.Services;

public static class Converters
{
    /// <summary>"Icon.Calendar" → StreamGeometry from application resources.</summary>
    public static readonly IValueConverter IconKey = new FuncValueConverter<string?, Geometry?>(key =>
        key is { Length: > 0 } && Application.Current?.TryFindResource(key, out var value) == true ? value as Geometry : null);

    public static readonly IValueConverter SidebarWidth = new FuncValueConverter<bool, double>(collapsed => collapsed ? 64 : 232);

    public static readonly IValueConverter Upper = new FuncValueConverter<string?, string?>(s => s?.ToUpperInvariant());

    /// <summary>Motion on → the section transition; off → none (TransitioningContentControl swaps instantly).
    /// The binding hands this converter Motion.Enabled — a bool, not the MotionSettings itself — so there is no
    /// instance here to take Duration(180) from; FadeSlide's own defaults are 180 ms out, 80 ms gap, 180 ms in.</summary>
    public static readonly IValueConverter PageTransition = new FuncValueConverter<bool, IPageTransition?>(enabled => enabled ? new Controls.FadeSlide() : null);

    /// <summary>Friend colour slot → Brush.Friend1..5 (theme-invariant tokens).</summary>
    public static readonly IValueConverter FriendBrush = new FuncValueConverter<int, IBrush?>(i =>
        Application.Current is { } app && app.TryGetResource($"Brush.Friend{Math.Clamp(i, 0, 4) + 1}", app.ActualThemeVariant, out var b) ? b as IBrush : null);

    /// <summary>Footer theme button: the Sun offers the light theme while it is dark, the Moon the other way round.</summary>
    public static readonly IValueConverter ThemeIcon = new FuncValueConverter<bool, Geometry?>(dark => Resource(dark ? "Icon.Sun" : "Icon.Moon"));

    /// <summary>Title-bar maximize button: the restore glyph while the window is maximized.</summary>
    public static readonly IValueConverter MaximizeIcon = new FuncValueConverter<bool, Geometry?>(maximized => Resource(maximized ? "Icon.Restore" : "Icon.Square"));

    /// <summary>The ≡ button sits at the right of the expanded sidebar and centred on the rail.</summary>
    public static readonly IValueConverter RailAlignment = new FuncValueConverter<bool, HorizontalAlignment>(collapsed => collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Right);

    private static Geometry? Resource(string key) => Application.Current?.TryFindResource(key, out var value) == true ? value as Geometry : null;
}
