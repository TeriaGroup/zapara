using Avalonia;
using Avalonia.Controls;

namespace Vograph.Desktop.Features.Chat;

public partial class ChatInboxView : UserControl
{
    private ChatInboxViewModel? watched;
    private bool attached;
    public ChatInboxView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        watched?.Watch(false);
        base.OnDataContextChanged(e);
        watched = DataContext as ChatInboxViewModel;
        watched?.Watch(IsVisible && attached);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        watched?.Watch(IsVisible);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        watched?.Watch(false);
        attached = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) watched?.Watch(IsVisible && attached);
    }
}
