using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Vograph.Desktop.Services;

public static class Converters
{
    /// <summary>"Icon.Calendar" → StreamGeometry from application resources.</summary>
    public static readonly IValueConverter IconKey = new FuncValueConverter<string?, Geometry?>(key =>
        key is { Length: > 0 } && Application.Current?.TryFindResource(key, out var value) == true ? value as Geometry : null);

    public static readonly IValueConverter SidebarWidth = new FuncValueConverter<bool, double>(collapsed => collapsed ? 64 : 232);

    public static readonly IValueConverter Upper = new FuncValueConverter<string?, string?>(s => s?.ToUpperInvariant());

    /// <summary>Friend colour slot → Brush.Friend1..5 (theme-invariant tokens).</summary>
    public static readonly IValueConverter FriendBrush = new FuncValueConverter<int, IBrush?>(i =>
        Application.Current is { } app && app.TryGetResource($"Brush.Friend{Math.Clamp(i, 0, 4) + 1}", app.ActualThemeVariant, out var b) ? b as IBrush : null);
}
