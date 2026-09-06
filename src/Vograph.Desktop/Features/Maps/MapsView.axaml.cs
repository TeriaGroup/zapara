using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Vograph.Desktop.Features.Maps;

public partial class MapsView : UserControl
{
    private MapsViewModel? _vm;

    public MapsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as MapsViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        };
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
