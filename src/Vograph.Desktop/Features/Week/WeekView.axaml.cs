using Avalonia.Controls;
using Avalonia;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Vograph.Desktop.Controls;

namespace Vograph.Desktop.Features.Week;

public partial class WeekView : UserControl
{
    private readonly PlannerTouchNavigation _swipe;
    private WeekViewModel? bound;

    public WeekView()
    {
        InitializeComponent();
        _swipe = new PlannerTouchNavigation(WeekContent,
            () => DataContext is WeekViewModel { IsLoaded: true, HasGroup: true, IsBusy: false },
            () => (DataContext, (DataContext as WeekViewModel)?.ParityIndex),
            direction =>
            {
                if (DataContext is WeekViewModel vm) vm.ParityIndex = direction > 0 ? 1 : 0;
            });
        DataContextChanged += (_, _) => { _swipe.Reset(); BindClipboard(); if (DataContext is WeekViewModel vm) vm.SetViewportWidth(Bounds.Width); };
        SizeChanged += (_, _) => { if (DataContext is WeekViewModel vm) vm.SetViewportWidth(Bounds.Width); };
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); BindClipboard(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { bound?.SetClipboardWriter(null); bound = null; base.OnDetachedFromVisualTree(e); }
    private void BindClipboard()
    {
        bound?.SetClipboardWriter(null);
        bound = this.IsAttachedToVisualTree() ? DataContext as WeekViewModel : null;
        bound?.SetClipboardWriter(async value =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("Clipboard unavailable");
            await clipboard.SetTextAsync(value);
        });
    }
}
