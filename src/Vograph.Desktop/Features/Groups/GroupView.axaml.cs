using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;

namespace Vograph.Desktop.Features.Groups;

public partial class GroupView : UserControl
{
    private GroupViewModel? boundGroup;
    private bool compactDetail;
    public GroupView()
    {
        InitializeComponent();
        SizeChanged += (_,_)=>ApplySpaceLayout();
    }
    private void ShowTopicList(object? sender,Avalonia.Interactivity.RoutedEventArgs e){compactDetail=false;ApplySpaceLayout();}
    private void GroupChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)
    {if(e.PropertyName==nameof(GroupViewModel.SelectedChannel)||e.PropertyName==nameof(GroupViewModel.IsDirect)){compactDetail=true;ApplySpaceLayout();}}
    private void ApplySpaceLayout()
    {
        var compact=Bounds.Width<760;
        SpaceColumns.ColumnDefinitions=new ColumnDefinitions(compact?"*":"280,12,*");
        Grid.SetColumn(SpaceDetail,compact?0:2);
        SpaceList.IsVisible=!compact||!compactDetail;
        SpaceDetail.IsVisible=!compact||compactDetail;
        TopicListButton.IsVisible=compact&&compactDetail;
    }
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
            previous.PropertyChanged-=GroupChanged;
            previous.Watch(false);
            previous.SetClipboardWriter(null);
        }
        boundGroup = DataContext as GroupViewModel;
        if (boundGroup is { } group) { group.PropertyChanged-=GroupChanged;group.PropertyChanged+=GroupChanged;group.Watch(IsVisible); BindClipboard(group); }
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
