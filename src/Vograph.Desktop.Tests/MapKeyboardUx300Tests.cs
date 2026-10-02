using Vograph.Desktop.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Input;
using Vograph.Desktop.Features.Maps;
using Vograph.Desktop.Services;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class MapKeyboardUx300Tests
{
    [AvaloniaFact]
    public void Keyboard_pan_uses_the_same_view_change_contract_as_drag()
    {
        var panel = new ZoomPanel { OffsetX = 10, OffsetY = 20 };
        var changes = 0; panel.ViewChanged += (_, _) => changes++;
        panel.PanBy(-40, 40);
        Assert.Equal(-30, panel.OffsetX);
        Assert.Equal(60, panel.OffsetY);
        Assert.Equal(1, changes);
    }

    [AvaloniaFact]
    public void Shift_plus_routed_to_focused_map_zooms_but_modified_arrows_do_not_pan()
    {
        Loc.Init(new I18nService());
        var view = new MapsView();
        var zoom = view.FindControl<ZoomPanel>("Zoom")!;
        zoom.IsVisible = true;
        zoom.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.OemPlus, KeyModifiers = KeyModifiers.Shift });
        Assert.True(zoom.Scale > 1);
        var x = zoom.OffsetX;
        zoom.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Left, KeyModifiers = KeyModifiers.Control });
        Assert.Equal(x, zoom.OffsetX);
    }
}
