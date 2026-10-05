using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Vograph.Desktop.Controls;

public class EmptyState : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(Title));
    public static readonly StyledProperty<string?> HintProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(Hint));
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<EmptyState, Geometry?>(nameof(Icon));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Hint { get => GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
}
