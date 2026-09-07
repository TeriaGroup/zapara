using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Vograph.Desktop.Features.Maps;

public partial class MapFullscreenView : UserControl
{
    private MapsViewModel? _vm;

    public MapFullscreenView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook((DataContext as MapFullscreenViewModel)?.Owner);
        Zoom.ViewChanged += (_, _) => PositionLabel(); // pan or zoom: the label follows the room it names
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Hook(null); // the overlay is rebuilt per fullscreen toggle; the section view model outlives it
    }

    private void Hook(MapsViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = vm;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        PositionLabel();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MapsViewModel.HasHighlight) or nameof(MapsViewModel.HighlightLeft) or nameof(MapsViewModel.HighlightTop)) PositionLabel();
    }

    /// <summary>Spec §5.5: the room label sits above the highlight but outside the zoom transform, so it keeps
    /// its size at any scale. Same arithmetic as MapsView, on the plan the overlay shows.</summary>
    private void PositionLabel()
    {
        if (_vm is not { HasHighlight: true }) return;
        var p = MapsComposer.LabelOffset(Zoom.Scale, Zoom.OffsetX, Zoom.OffsetY, _vm.HighlightLeft, _vm.HighlightTop);
        HighlightLabel.Margin = new Thickness(p.X, p.Y, 0, 0);
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Zoom.ZoomIn();
    private void OnZoomOut(object? sender, RoutedEventArgs e) => Zoom.ZoomOut();
    private void OnFit(object? sender, RoutedEventArgs e) => Zoom.Fit();
}
