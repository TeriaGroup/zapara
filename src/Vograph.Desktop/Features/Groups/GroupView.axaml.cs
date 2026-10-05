using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.Specialized;

namespace Vograph.Desktop.Features.Groups;

public partial class GroupView : UserControl
{
    private GroupViewModel? boundGroup;
    private bool compactDetail;
    private double topicListOffset;
    private bool preserveOlderMessages;
    private double olderExtent;
    private double olderOffset;
    public GroupView()
    {
        InitializeComponent();
        SizeChanged += (_,_)=>ApplySpaceLayout();
        MessagesScroll.LayoutUpdated += (_, _) => ApplyOlderMessagePosition();
    }
    private void ShowTopicList(object? sender,Avalonia.Interactivity.RoutedEventArgs e)
    {
        compactDetail=false;ApplySpaceLayout();
        Dispatcher.UIThread.Post(() =>
        {
            if (Bounds.Width >= 760 || compactDetail || DataContext is not GroupViewModel group) return;
            TopicScroll.Offset = new Vector(TopicScroll.Offset.X,
                Math.Clamp(topicListOffset, 0, Math.Max(0, TopicScroll.Extent.Height - TopicScroll.Viewport.Height)));
            var selected = TopicScroll.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.DataContext is GroupChannelRow row && row.IsSelected);
            selected?.BringIntoView(); selected?.Focus();
        }, DispatcherPriority.Loaded);
    }
    private void GroupChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GroupViewModel.SelectedChannel) or nameof(GroupViewModel.IsDirect))
        { if (Bounds.Width < 760 && !compactDetail) topicListOffset = TopicScroll.Offset.Y;
            compactDetail=true; ApplySpaceLayout(); }
        if (e.PropertyName == nameof(GroupViewModel.HasHome) && DataContext is GroupViewModel { HasHome: false }) topicListOffset = 0;
    }
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
    private void OnGroupDraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (e.KeyModifiers & KeyModifiers.Control) == 0 ||
            (e.KeyModifiers & KeyModifiers.Alt) != 0 || DataContext is not GroupViewModel group ||
            !group.SendCommand.CanExecute(null)) return;
        e.Handled = true;
        group.SendCommand.Execute(null);
    }
    private void OpenMessageActions(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control control)
            control.GetVisualAncestors().OfType<HoldBox>().FirstOrDefault()?.Choose(null);
    }
    private void FocusQuoteTarget(GroupMessageRow target)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is not GroupViewModel group || !group.Messages.Contains(target)) return;
            var bubble = MessagesScroll.GetVisualDescendants().OfType<HoldBox>()
                .FirstOrDefault(control => ReferenceEquals(control.DataContext, target));
            bubble?.BringIntoView();
            bubble?.Focus();
        }, DispatcherPriority.Loaded);
    }
    private void FocusObligation(string kind, Guid id)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is not GroupViewModel group) return;
            var target = this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Focusable &&
                (kind == "homework" && control.DataContext is SpaceHomeworkRow hw && hw.Item.HomeworkId == id ||
                 kind == "form" && control.DataContext is SpaceFormRow form && form.Form.FormId == id ||
                 kind == "ballot" && control.DataContext is GroupBallotRow ballot && ballot.BallotId == id));
            target?.BringIntoView(); target?.Focus();
        }, DispatcherPriority.Loaded);
    }
    private void OnGroupMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset) { preserveOlderMessages = false; return; }
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null || e.NewStartingIndex != 0 ||
            boundGroup is null || boundGroup.Messages.Count <= e.NewItems.Count || preserveOlderMessages) return;
        olderExtent = MessagesScroll.Extent.Height;
        olderOffset = MessagesScroll.Offset.Y;
        preserveOlderMessages = true;
    }
    private void ApplyOlderMessagePosition()
    {
        if (!preserveOlderMessages || MessagesScroll.Extent.Height <= olderExtent) return;
        preserveOlderMessages = false;
        MessagesScroll.Offset = new Vector(MessagesScroll.Offset.X,
            olderOffset + MessagesScroll.Extent.Height - olderExtent);
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
            previous.Messages.CollectionChanged -= OnGroupMessagesChanged;
            previous.ObligationFocusRequested -= FocusObligation;
            previous.PropertyChanged-=GroupChanged;
            previous.QuoteTargetRequested-=FocusQuoteTarget;
            previous.Watch(false);
            previous.SetClipboardWriter(null);
        }
        boundGroup = DataContext as GroupViewModel;
        topicListOffset = 0;
        preserveOlderMessages = false;
        if (boundGroup is { } group) { group.ObligationFocusRequested-=FocusObligation;group.ObligationFocusRequested+=FocusObligation;group.Messages.CollectionChanged-=OnGroupMessagesChanged;group.Messages.CollectionChanged+=OnGroupMessagesChanged;group.PropertyChanged-=GroupChanged;group.PropertyChanged+=GroupChanged;group.QuoteTargetRequested-=FocusQuoteTarget;group.QuoteTargetRequested+=FocusQuoteTarget;group.Watch(IsVisible); BindClipboard(group); }
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is GroupViewModel group) { group.ObligationFocusRequested-=FocusObligation;group.ObligationFocusRequested+=FocusObligation;group.Messages.CollectionChanged-=OnGroupMessagesChanged;group.Messages.CollectionChanged+=OnGroupMessagesChanged;group.QuoteTargetRequested-=FocusQuoteTarget; group.QuoteTargetRequested+=FocusQuoteTarget; group.Watch(true); BindClipboard(group); }
    }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is GroupViewModel group) { group.Watch(false); group.SetClipboardWriter(null); group.ObligationFocusRequested-=FocusObligation; group.Messages.CollectionChanged-=OnGroupMessagesChanged; group.QuoteTargetRequested-=FocusQuoteTarget; }
        base.OnDetachedFromVisualTree(e);
    }
}
