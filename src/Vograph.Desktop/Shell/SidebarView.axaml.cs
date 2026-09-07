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
            return;
        }
        var height = Math.Max(0, active.Bounds.Height - 18);
        var currentY = NavIndicator.RenderTransform is TransformOperations t ? t.Value.M32 : double.NaN;
        if (NavIndicator.IsVisible && Math.Abs(NavIndicator.Height - height) < 0.5 && Math.Abs(currentY - top.Y) < 0.5) return;

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
