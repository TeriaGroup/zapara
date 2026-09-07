using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Vograph.Desktop.Features.Maps;

public partial class MapsView : UserControl
{
    private MapsViewModel? _vm;

    public MapsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { if (this.IsAttachedToVisualTree()) Hook(DataContext as MapsViewModel); };
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
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Zoom.ZoomIn();
    private void OnZoomOut(object? sender, RoutedEventArgs e) => Zoom.ZoomOut();
    private void OnFit(object? sender, RoutedEventArgs e) => Zoom.Fit();
    private void OnReset(object? sender, RoutedEventArgs e) => Zoom.ResetScale();
}
