using Avalonia;
using Avalonia.Controls;

namespace Vograph.Desktop.Features.Groups;

public partial class GroupView : UserControl
{
    public GroupView() => InitializeComponent();
    private void JumpToLatest(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => MessagesScroll.Offset = new Vector(MessagesScroll.Offset.X,
            Math.Max(0, MessagesScroll.Extent.Height - MessagesScroll.Viewport.Height));
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is GroupViewModel group) group.Watch(IsVisible);
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is GroupViewModel group) group.Watch(true);
    }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is GroupViewModel group) group.Watch(false);
        base.OnDetachedFromVisualTree(e);
    }
}
