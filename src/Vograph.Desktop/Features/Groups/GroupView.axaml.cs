using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;

namespace Vograph.Desktop.Features.Groups;

public partial class GroupView : UserControl
{
    private GroupViewModel? boundGroup;
    public GroupView() => InitializeComponent();
    private void JumpToLatest(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => MessagesScroll.Offset = new Vector(MessagesScroll.Offset.X,
            Math.Max(0, MessagesScroll.Extent.Height - MessagesScroll.Viewport.Height));
    private void OpenMessageActions(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control control)
            control.GetVisualAncestors().OfType<HoldBox>().FirstOrDefault()?.Choose(null);
    }
    private void BindClipboard(GroupViewModel group) => group.SetClipboardWriter(async value =>
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
            ?? throw new InvalidOperationException("Clipboard unavailable");
        await clipboard.SetTextAsync(value);
    });
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (boundGroup is { } previous && !ReferenceEquals(previous, DataContext))
        {
            previous.Watch(false);
            previous.SetClipboardWriter(null);
        }
        boundGroup = DataContext as GroupViewModel;
        if (boundGroup is { } group) { group.Watch(IsVisible); BindClipboard(group); }
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is GroupViewModel group) { group.Watch(true); BindClipboard(group); }
    }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is GroupViewModel group) { group.Watch(false); group.SetClipboardWriter(null); }
        base.OnDetachedFromVisualTree(e);
    }
}
