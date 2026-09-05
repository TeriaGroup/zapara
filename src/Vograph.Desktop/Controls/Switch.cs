using Avalonia.Controls.Primitives;

namespace Vograph.Desktop.Controls;

/// <summary>Toggle drawn as a pill switch. A ToggleButton subclass: no required template parts, no drag logic to fight.</summary>
public class Switch : ToggleButton
{
    protected override Type StyleKeyOverride => typeof(Switch);
}
