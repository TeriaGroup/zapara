using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using Vograph.Desktop.Controls;

namespace Vograph.Desktop.Shell;

public partial class SidebarView : UserControl
{
    private ShellViewModel? _vm;
    private bool _placed;

    /// <summary>The last target handed to the indicator, not the indicator's current geometry: Height and
    /// RenderTransform hold mid-transition values while the bar slides, and TransformOperations has no value
    /// equality, so a guard that read them re-assigned a fresh target on every layout pass and the bar crawled
    /// behind the click instead of sliding to it. Same shape as SegmentedControl.PositionThumb's _lastX.</summary>
    private double _lastY = double.NaN;
    private double _lastHeight = double.NaN;

    public SidebarView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook(DataContext as ShellViewModel);
        LayoutUpdated += (_, _) => PositionIndicator();
    }

    private void Hook(ShellViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = vm;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.CurrentKey) or nameof(ShellViewModel.SidebarCollapsed)) PositionIndicator();
    }

    /// <summary>Spec §7: one 3px bar that slides between items. Placed in Root's coordinates from the active NavItem's
    /// bounds (9px inset top and bottom, like the old per-item bar). The first placement is not animated — the same
    /// rule as the segmented control's thumb — so a fresh window never shows the bar sliding in from the top.</summary>
    private void PositionIndicator()
    {
        var active = this.GetVisualDescendants().OfType<NavItem>().FirstOrDefault(n => n.IsActive && n.IsEffectivelyVisible);
        if (active is null || active.Bounds.Height <= 0 || active.TranslatePoint(new Point(0, 9), Root) is not { } top)
        {
            NavIndicator.IsVisible = false;
            _lastY = _lastHeight = double.NaN; // force a placement when the active item comes back
            return;
        }
        var height = Math.Max(0, active.Bounds.Height - 18);
        if (NavIndicator.IsVisible && Math.Abs(_lastHeight - height) < 0.5 && Math.Abs(_lastY - top.Y) < 0.5) return;
        _lastY = top.Y;
        _lastHeight = height;

        if (!_placed) NavIndicator.Transitions = null; // a local null hides the styled transitions for the first placement
        NavIndicator.IsVisible = true;
        NavIndicator.Height = height;
        NavIndicator.RenderTransform = TransformOperations.Parse($"translateY({top.Y.ToString(CultureInfo.InvariantCulture)}px)");
        if (!_placed)
        {
            NavIndicator.ClearValue(Animatable.TransitionsProperty);
            _placed = true;
        }
    }
}
