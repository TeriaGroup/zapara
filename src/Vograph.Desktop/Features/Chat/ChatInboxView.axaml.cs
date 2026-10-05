using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.Specialized;

namespace Vograph.Desktop.Features.Chat;

public partial class ChatInboxView : UserControl
{
    private enum ScrollAction { None, Latest, Preserve }
    private ChatInboxViewModel? watched;
    private bool attached;
    private bool latestAfterReset = true;
    private ScrollAction pendingScroll;
    private double oldExtent;
    private double oldOffset;
    private void BindClipboard(ChatInboxViewModel model) => model.SetClipboardWriter(async value =>
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("Clipboard unavailable");
        await clipboard.SetTextAsync(value);
    });
    public ChatInboxView()
    {
        InitializeComponent();
        MessageScroll.LayoutUpdated += (_, _) => ApplyPendingScroll();
        MessageScroll.PropertyChanged += (_, change) =>
        {
            if (change.Property == ScrollViewer.OffsetProperty) UpdateJumpVisibility();
        };
    }

    private void JumpToLatest(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        pendingScroll = ScrollAction.None;
        MessageScroll.Offset = new Vector(MessageScroll.Offset.X,
            Math.Max(0, MessageScroll.Extent.Height - MessageScroll.Viewport.Height));
    }
    private void JumpToInvites(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { InviteList.BringIntoView(); InviteList.Focus(); }

    private void OnDraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (e.KeyModifiers & KeyModifiers.Control) == 0 ||
            (e.KeyModifiers & KeyModifiers.Alt) != 0 || DataContext is not ChatInboxViewModel chat ||
            !chat.SendCommand.CanExecute(null)) return;
        e.Handled = true;
        chat.SendCommand.Execute(null);
    }

    private void FocusQuoteTarget(ChatMessageRow target)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (watched is null || !watched.Messages.Contains(target)) return;
            var bubble = MessageScroll.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(control => control.Classes.Contains("chat-message") && ReferenceEquals(control.DataContext, target));
            bubble?.BringIntoView();
            bubble?.Focus();
        }, DispatcherPriority.Loaded);
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (watched is null) return;
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            pendingScroll = ScrollAction.None;
            latestAfterReset = true;
            watched.ShowJumpLatest = false;
            return;
        }
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null) return;
        var previousCount = watched.Messages.Count - e.NewItems.Count;
        if (previousCount > 0 && e.NewStartingIndex == 0 && !latestAfterReset)
        {
            if (pendingScroll != ScrollAction.Preserve)
            {
                oldExtent = MessageScroll.Extent.Height;
                oldOffset = MessageScroll.Offset.Y;
                pendingScroll = ScrollAction.Preserve;
            }
            return;
        }
        var follow = latestAfterReset || ChatScrollLogic.NearLatest(MessageScroll.Extent.Height,
            MessageScroll.Viewport.Height, MessageScroll.Offset.Y);
        latestAfterReset = false;
        if (follow && pendingScroll != ScrollAction.Preserve) pendingScroll = ScrollAction.Latest;
    }

    private void ApplyPendingScroll()
    {
        var action = pendingScroll;
        if (action == ScrollAction.None) { UpdateJumpVisibility(); return; }
        pendingScroll = ScrollAction.None;
        var target = action == ScrollAction.Latest
            ? Math.Max(0, MessageScroll.Extent.Height - MessageScroll.Viewport.Height)
            : ChatScrollLogic.AfterPrepend(oldOffset, oldExtent, MessageScroll.Extent.Height);
        MessageScroll.Offset = new Vector(MessageScroll.Offset.X, target);
        UpdateJumpVisibility();
    }

    private void UpdateJumpVisibility()
    {
        if (watched is null) return;
        watched.ShowJumpLatest = watched.HasConversation && !ChatScrollLogic.NearLatest(
            MessageScroll.Extent.Height, MessageScroll.Viewport.Height, MessageScroll.Offset.Y);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (watched is not null) { watched.Messages.CollectionChanged -= OnMessagesChanged; watched.QuoteTargetRequested -= FocusQuoteTarget; watched.MessageFocusRequested -= FocusQuoteTarget; }
        watched?.SetClipboardWriter(null);
        watched?.Watch(false);
        base.OnDataContextChanged(e);
        watched = DataContext as ChatInboxViewModel;
        if (watched is not null) { watched.Messages.CollectionChanged += OnMessagesChanged; watched.MessageFocusRequested += FocusQuoteTarget; }
        if (attached && watched is not null) BindClipboard(watched);
        pendingScroll = ScrollAction.None;
        latestAfterReset = true;
        if (watched is not null) watched.ShowJumpLatest = false;
        if (attached && watched?.Messages.Count > 0) pendingScroll = ScrollAction.Latest;
        watched?.Watch(IsVisible && attached);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        if (watched is not null) { watched.MessageFocusRequested -= FocusQuoteTarget; watched.MessageFocusRequested += FocusQuoteTarget; BindClipboard(watched); }
        if (watched?.Messages.Count > 0) pendingScroll = ScrollAction.Latest;
        watched?.Watch(IsVisible);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        watched?.Watch(false);
        if (watched is not null) watched.MessageFocusRequested -= FocusQuoteTarget;
        watched?.SetClipboardWriter(null);
        attached = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) watched?.Watch(IsVisible && attached);
    }
}
