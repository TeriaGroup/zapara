using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Vograph.Desktop.Controls;

/// <summary>Sidebar navigation entry. Pseudo-classes: :active (current section), :compact (icon-only rail).</summary>
public class NavItem : Button
{
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<NavItem, Geometry?>(nameof(Icon));
    public static readonly StyledProperty<string?> BadgeProperty = AvaloniaProperty.Register<NavItem, string?>(nameof(Badge));
    public static readonly StyledProperty<bool> IsActiveProperty = AvaloniaProperty.Register<NavItem, bool>(nameof(IsActive));
    public static readonly StyledProperty<bool> IsCompactProperty = AvaloniaProperty.Register<NavItem, bool>(nameof(IsCompact));

    protected override Type StyleKeyOverride => typeof(NavItem);

    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? Badge { get => GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public bool IsCompact { get => GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty) PseudoClasses.Set(":active", change.GetNewValue<bool>());
        else if (change.Property == IsCompactProperty) PseudoClasses.Set(":compact", change.GetNewValue<bool>());
    }
}
