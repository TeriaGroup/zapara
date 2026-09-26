using Avalonia.Controls;
using Avalonia.Controls.Primitives;
namespace Vograph.Desktop.Controls;
/// <summary>A button theme with the toggle state exposed to assistive technology.</summary>
public sealed class SelectionButton:ToggleButton
{
    protected override Type StyleKeyOverride=>typeof(Button);
}
