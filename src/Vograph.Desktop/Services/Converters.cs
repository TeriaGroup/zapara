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
}
