using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Vograph.Desktop.Features.Maps;

public partial class MapFullscreenView : UserControl
{
    public MapFullscreenView() => InitializeComponent();

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Zoom.ZoomIn();
    private void OnZoomOut(object? sender, RoutedEventArgs e) => Zoom.ZoomOut();
    private void OnFit(object? sender, RoutedEventArgs e) => Zoom.Fit();
}
