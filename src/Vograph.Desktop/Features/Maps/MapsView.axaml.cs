using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Input;

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
        Zoom.ViewChanged += (_, _) => { PositionLabel(); PositionStairs(); };
        Zoom.SizeChanged += (_, _) => PositionLabel();
        HighlightLabel.SizeChanged += (_, _) => PositionLabel();
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
        if (ReferenceEquals(_vm, vm)) { PositionStairs(); PositionLabel(); return; }
        if (_vm is not null) { _vm.PropertyChanged -= OnVmChanged; _vm.SetClipboardWriter(null); }
        _vm = vm;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmChanged;
            _vm.SetClipboardWriter(async value =>
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("Clipboard unavailable");
                await clipboard.SetTextAsync(value);
            });
        }
        PositionStairs();
        PositionLabel();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapsViewModel.Image)) { Zoom.RequestFit(); PositionStairs(); }
        if (e.PropertyName is nameof(MapsViewModel.HasHighlight) or nameof(MapsViewModel.HighlightLeft) or nameof(MapsViewModel.HighlightTop)) PositionLabel();
    }

    private void PositionStairs()
    {
        StairMarkers.ImageSize = _vm?.Image?.PixelSize ?? default;
        StairMarkers.Scale = Zoom.Scale;
        StairMarkers.OffsetX = Zoom.OffsetX;
        StairMarkers.OffsetY = Zoom.OffsetY;
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
    private void OnMapKeyDown(object? sender, KeyEventArgs e)
    {
        if (!Zoom.IsVisible || e.KeyModifiers != KeyModifiers.None &&
            !(e.Key == Key.OemPlus && e.KeyModifiers == KeyModifiers.Shift)) return;
        switch (e.Key)
        {
            case Key.Add or Key.OemPlus: Zoom.ZoomIn(); break;
            case Key.Subtract or Key.OemMinus: Zoom.ZoomOut(); break;
            case Key.Left: Zoom.PanBy(40, 0); break;
            case Key.Right: Zoom.PanBy(-40, 0); break;
            case Key.Up: Zoom.PanBy(0, 40); break;
            case Key.Down: Zoom.PanBy(0, -40); break;
            default: return;
        }
        e.Handled = true;
    }
}
