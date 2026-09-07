using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Vograph.Desktop.Features.Maps;

public partial class MapsView : UserControl
{
    private MapsViewModel? _vm;

    /// <summary>What moves the label, instead of its Margin: a Margin change invalidates the label's own measure,
    /// and its changed desired size then drags the map panel — ZoomPanel, transform and all — through a layout pass
    /// on every frame of a pan or a zoom. A render transform only repaints (T10-R6).</summary>
    private readonly TranslateTransform _labelAt = new();

    public MapsView()
    {
        InitializeComponent();
        HighlightLabel.RenderTransform = _labelAt;
        DataContextChanged += (_, _) => { if (this.IsAttachedToVisualTree()) Hook(DataContext as MapsViewModel); };
        Zoom.ViewChanged += (_, _) => PositionLabel(); // pan or zoom: the label follows the room it names
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Hook(DataContext as MapsViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Hook(null); // a view per navigation must not pile up handlers on the long-lived section view model
    }

    private void Hook(MapsViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = vm;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapsViewModel.Image)) Zoom.RequestFit();
        if (e.PropertyName is nameof(MapsViewModel.HasHighlight) or nameof(MapsViewModel.HighlightLeft) or nameof(MapsViewModel.HighlightTop)) PositionLabel();
    }

    /// <summary>Spec §5.5: the room label sits above the highlight but outside the zoom transform, so it keeps
    /// its size at any scale. MapsComposer.LabelOffset does the arithmetic; this only carries the result over.</summary>
    private void PositionLabel()
    {
        if (_vm is not { HasHighlight: true }) return;
        var p = MapsComposer.LabelOffset(Zoom.Scale, Zoom.OffsetX, Zoom.OffsetY, _vm.HighlightLeft, _vm.HighlightTop,
            Zoom.Bounds.Size, HighlightLabel.Bounds.Size);
        _labelAt.X = p.X;
        _labelAt.Y = p.Y;
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Zoom.ZoomIn();
    private void OnZoomOut(object? sender, RoutedEventArgs e) => Zoom.ZoomOut();
    private void OnFit(object? sender, RoutedEventArgs e) => Zoom.Fit();
    private void OnReset(object? sender, RoutedEventArgs e) => Zoom.ResetScale();
}
