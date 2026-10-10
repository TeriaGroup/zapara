using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Communities;
using Vograph.Core.Services.Social;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;
using Zapara.Contracts.Social;

namespace Vograph.Desktop.Features.Chat;

public sealed partial class ChatInboxViewModel : ViewModelBase
{
    private readonly ShellViewModel shell;
    private readonly SocialHttpClient? client;
    private readonly CommunityHttpClient? communityClient;
    private readonly Func<CancellationToken, Task<string?>>? access;
    private readonly IChatMediaRecorder recorder;
    private readonly ChatMediaPlayer player = new();
    private readonly Dictionary<Guid, Bitmap> previewCache = [];
    private readonly HashSet<Guid> previewLoading = [];
    private CancellationTokenSource previewCancellation = new();
    private DispatcherTimer? recordingTimer;
    private DateTimeOffset recordingStarted;
    private string? recordingKind;
    private Guid? playingMessage;
    private int generation;
    private Guid? conversationId;
    private Guid? replyTo;
    private Guid? editing;
    private Guid? peerId;
    private int unreadAtOpen;
    private DateTimeOffset? unreadSnapshotLastAt;
    private DateTimeOffset lastChatIdentityCheck;
    private DispatcherTimer? timer;
    private bool watching;
    private int polling;
    private Func<string, Task>? clipboardWriter;
    private readonly HashSet<Guid> resolvingInvites = [];
    public void SetClipboardWriter(Func<string, Task>? writer) => clipboardWriter = writer;
    internal TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(12);
    public void Watch(bool visible)
    {
        if (watching == visible) return;
        watching = visible;
        timer?.Stop();
        timer = null;
        if (!visible) return;
        timer = new DispatcherTimer { Interval = PollInterval };
        timer.Tick += OnTick;
        timer.Start();
    }

    public override void Detach()
    {
        Watch(false);
        _ = CancelRecordingAsync();
        StopPlayback();
        ReleaseVisibleAvatars();
        clipboardWriter = null;
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (!watching || Interlocked.Exchange(ref polling, 1) != 0) return;
        try
        {
            await RefreshCoreAsync(background: true);
            if (watching) await PullLatestAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Log.Error("chat inbox poll", ex); }
        finally { Interlocked.Exchange(ref polling, 0); }
    }

    private SocialHttpClient? Social => client ?? App.Social;
    private CommunityHttpClient? Communities => communityClient ?? App.Communities;
    private Func<CancellationToken, Task<string?>>? Access => access ?? App.SocialAccess ?? App.CommunityAccess;

    public ChatInboxViewModel(AppServices app, ShellViewModel shell, SocialHttpClient? client = null,
        CommunityHttpClient? communityClient = null, Func<CancellationToken, Task<string?>>? access = null,
        IChatMediaRecorder? recorder = null) : base(app)
    {
        this.shell = shell;
        this.client = client;
        this.communityClient = communityClient;
        this.access = access;
        this.recorder = recorder ?? new WindowsChatMediaRecorder();
        player.PlaybackEnded += () => Dispatcher.UIThread.Post(StopPlayback);
        player.PlaybackFailed += () => Dispatcher.UIThread.Post(() =>
        {
            if (playingMessage is null) return;
            StopPlayback();
            Status = "Не удалось воспроизвести запись.";
        });
        NeedAccount = (Social is null && Communities is null) || Access is null;
        Chats.CollectionChanged += (_, _) => RefreshInboxBrowse();
        Incoming.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(HasIncoming)); OnPropertyChanged(nameof(IncomingCaption)); };
        Messages.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(NoMessages)); RefreshMessageBrowse(); GroupMessages(); };
    }

    public ObservableCollection<ChatInboxRow> Chats { get; } = [];
    private IReadOnlyList<ChatInboxRow> filteredChats = [];
    [ObservableProperty] private string inboxSearch = "";
    [ObservableProperty] private int inboxSourceIndex;
    [ObservableProperty] private bool unreadOnly;
    [ObservableProperty] private bool loadingInbox;
    public IReadOnlyList<ChatInboxRow> FilteredChats => filteredChats;
    public int UnreadTotal => ChatInboxBrowse.UnreadTotal(Chats);
    public string UnreadSummary => UnreadTotal == 0 ? "Нет непрочитанных" : $"Непрочитанных: {UnreadTotal}";
    public string InboxResultCount => $"Показано {filteredChats.Count} из {Chats.Count}";
    public bool HasInboxFilters => InboxSearch.Trim().Length > 0 || InboxSourceIndex is >= 1 and <= 3 || UnreadOnly;
    public bool NoInboxMatches => InboxLoaded && !LoadingInbox && !InboxLoadFailed && HasInboxFilters && filteredChats.Count == 0;
    public bool NoChats => InboxLoaded && !LoadingInbox && !InboxLoadFailed && Chats.Count == 0 && !HasInboxFilters;
    partial void OnInboxSearchChanged(string value) => RefreshInboxBrowse();
    partial void OnInboxSourceIndexChanged(int value) => RefreshInboxBrowse();
    partial void OnUnreadOnlyChanged(bool value) => RefreshInboxBrowse();
    partial void OnLoadingInboxChanged(bool value)
    {
        OnPropertyChanged(nameof(NoChats));
        OnPropertyChanged(nameof(NoInboxMatches));
    }
    [RelayCommand]
    private void ResetInboxFilters()
    {
        InboxSearch = "";
        InboxSourceIndex = 0;
        UnreadOnly = false;
    }
    private void RefreshInboxBrowse()
    {
        filteredChats = ChatInboxBrowse.Filter(Chats, InboxSearch, InboxSourceIndex, UnreadOnly);
        OnPropertyChanged(nameof(FilteredChats));
        OnPropertyChanged(nameof(UnreadTotal));
        OnPropertyChanged(nameof(UnreadSummary));
        OnPropertyChanged(nameof(InboxResultCount));
        OnPropertyChanged(nameof(HasInboxFilters));
        OnPropertyChanged(nameof(NoInboxMatches));
        OnPropertyChanged(nameof(NoChats));
    }
    public ObservableCollection<ChatInviteRow> Incoming { get; } = [];
    public bool HasIncoming => Incoming.Count > 0;
    public string IncomingCaption => $"Приглашений: {Incoming.Count}";
    public ObservableCollection<ChatMessageRow> Messages { get; } = [];
    public event Action<ChatMessageRow>? QuoteTargetRequested;
    public event Action<ChatMessageRow>? MessageFocusRequested;
    [ObservableProperty] private string unreadJumpFeedback = "";
    public bool HasUnreadJump => unreadAtOpen > 0 && unreadSnapshotLastAt is not null && conversationId is not null;
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task JumpFirstUnread()
    {
        if (!HasUnreadJump || conversationId is not Guid id) return;
        var ticket = generation;
        ChatMessageRow[] SnapshotIncoming() => Messages.Where(row => row.IsIncoming &&
            (unreadSnapshotLastAt is null || row.CreatedAt <= unreadSnapshotLastAt)).ToArray();
        for (var page = 0; page < 5 && HasMore && Messages.Count > 0 &&
            SnapshotIncoming().Length < unreadAtOpen; page++)
        {
            var before = Messages.Count;
            await LoadMessagesAsync(ticket, Messages[0].Id);
            if (conversationId != id || generation != ticket) return;
            if (Messages.Count == before) break;
        }
        var incoming = SnapshotIncoming();
        if (incoming.Length < unreadAtOpen)
        { UnreadJumpFeedback = HasMore ? "Начало непрочитанной области ещё раньше. Нажмите снова для следующих страниц." :
            "Счётчик изменился после открытия беседы. Обновите список бесед."; return; }
        var target = incoming[^unreadAtOpen];
        if (!VisibleMessages.Contains(target)) MessageSearch = "";
        foreach (var row in Messages) row.IsUnreadTarget = ReferenceEquals(row, target);
        UnreadJumpFeedback = "Показано начало непрочитанной области по счётчику при открытии беседы.";
        MessageFocusRequested?.Invoke(target);
    }
    [ObservableProperty] private string quoteFeedback = "";
    [ObservableProperty] private bool needAccount;
    partial void OnNeedAccountChanged(bool value) { if (value) { MyCode = ""; resolvingInvites.Clear(); } RaiseSignIn(); }
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string myCode = "";
    [ObservableProperty] private string messageSearch = "";
    public bool HasMessageSearch => !string.IsNullOrWhiteSpace(MessageSearch);
    [ObservableProperty] private string historySearchFeedback = "";
    public bool CanSearchOlder => HasMore && !string.IsNullOrWhiteSpace(MessageSearch);
    partial void OnHasMoreChanged(bool value) => OnPropertyChanged(nameof(CanSearchOlder));
    public IReadOnlyList<ChatMessageRow> VisibleMessages => string.IsNullOrWhiteSpace(MessageSearch)
        ? Messages.ToArray() : Messages.Where(row => row.SearchableText.Contains(MessageSearch.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    public bool NoHistoryMatches => MessagesLoaded && !LoadingMessages && !MessageLoadFailed && Messages.Count > 0 && VisibleMessages.Count == 0;
    public string HistorySearchScope => $"Поиск среди загруженных сообщений: {VisibleMessages.Count} из {Messages.Count}";
    partial void OnMessageSearchChanged(string value)
    { HistorySearchFeedback = ""; RefreshMessageBrowse(); OnPropertyChanged(nameof(CanSearchOlder)); OnPropertyChanged(nameof(HasMessageSearch)); }
    [RelayCommand] private void OpenSearchResult(ChatMessageRow? row)
    {
        if (row is null || !Messages.Contains(row) || !VisibleMessages.Contains(row) || !HasMessageSearch) return;
        MessageSearch = "";
        MessageFocusRequested?.Invoke(row);
    }
    [RelayCommand] private void ClearMessageSearch() => MessageSearch = "";
    [RelayCommand] private async Task JumpQuote(ChatMessageRow? source)
    {
        if (source is null || !Messages.Contains(source) || source.ReplyToId is not Guid parentId) return;
        var id = conversationId; var ticket = generation;
        for (var page = 0; page < 5 && Messages.All(row => row.Id != parentId) && HasMore && Messages.Count > 0; page++)
        {
            var before = Messages.Count;
            await LoadMessagesAsync(ticket, Messages[0].Id);
            if (conversationId != id || generation != ticket || !Messages.Contains(source)) return;
            if (Messages.Count == before) break;
        }
        foreach (var row in Messages) row.QuoteHint = "";
        var target = Messages.FirstOrDefault(row => row.Id == parentId);
        if (target is null) { source.QuoteHint = QuoteFeedback = HasMore ? "Ранее сообщение ещё не найдено. Нажмите цитату снова для следующих страниц." : "Цитата недоступна в этой истории."; return; }
        if (target.Deleted) { source.QuoteHint = QuoteFeedback = "Цитируемое сообщение удалено."; return; }
        if (!VisibleMessages.Contains(target)) MessageSearch = "";
        foreach (var row in Messages) row.IsQuoteTarget = ReferenceEquals(row, target);
        QuoteFeedback = "Цитируемое сообщение найдено.";
        QuoteTargetRequested?.Invoke(target);
        MessageFocusRequested?.Invoke(target);
    }
    private void RefreshMessageBrowse()
    {
        OnPropertyChanged(nameof(VisibleMessages)); OnPropertyChanged(nameof(NoHistoryMatches)); OnPropertyChanged(nameof(HistorySearchScope));
    }
    [RelayCommand] private void OpenAccount() => shell.OpenAccountSettings();
    [RelayCommand]
    private async Task CopyOwnCode()
    {
        var code = MyCode.Trim();
        if (NeedAccount || code.Length == 0 || clipboardWriter is null) return;
        try { await clipboardWriter(code); if (!NeedAccount && MyCode.Trim() == code) Status = "Код скопирован."; }
        catch { if (!NeedAccount) Status = "Не удалось скопировать код."; }
    }
    [ObservableProperty] private string inviteCode = "";
    [ObservableProperty] private string chatTitle = "";
    [ObservableProperty] private Bitmap? chatAvatar;
    public string ChatInitials => AvatarInitials.FromName(ChatTitle);
    public bool HasChatAvatar => ChatAvatar is not null;
    public bool NoChatAvatar => ChatAvatar is null;
    partial void OnChatTitleChanged(string value) => OnPropertyChanged(nameof(ChatInitials));
    partial void OnChatAvatarChanged(Bitmap? value)
    { OnPropertyChanged(nameof(HasChatAvatar)); OnPropertyChanged(nameof(NoChatAvatar)); }
    [ObservableProperty] private string draft = "";
    [ObservableProperty] private string actionCaption = "";
    [ObservableProperty] private bool hasMore;
    [ObservableProperty] private bool showJumpLatest;
    [ObservableProperty] private bool isRecording;
    [ObservableProperty] private bool isFinalizingRecording;
    [ObservableProperty] private string recordingCaption = "";
    public bool HasConversation => conversationId is not null;
    public bool CanAttachMedia => !Sending && !IsRecording && !IsFinalizingRecording && editing is null;

    partial void OnIsRecordingChanged(bool value)
    {
        StartRecordingCommand.NotifyCanExecuteChanged();
        AttachCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanAttachMedia));
        SendCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsFinalizingRecordingChanged(bool value)
    {
        StartRecordingCommand.NotifyCanExecuteChanged();
        AttachCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanAttachMedia));
        SendCommand.NotifyCanExecuteChanged();
    }

    public override Task ActivateAsync() { RaiseSignIn(); return RefreshCoreAsync(background: false); }
    /// <summary>Без входа (#18, D-02): понятное объяснение и кнопка «Войти» — только если вход реально работает.</summary>
    public bool SignInWorks => App.Shared.AccountPanel.SignInWorks;
    public bool ShowSignIn => NeedAccount && SignInWorks;
    public string NeedAccountHint => SignInWorks ? "Личные чаты и чаты групп открываются после входа в аккаунт." : T("accountUnconfigured");
    private void RaiseSignIn() { OnPropertyChanged(nameof(SignInWorks)); OnPropertyChanged(nameof(ShowSignIn)); OnPropertyChanged(nameof(NeedAccountHint)); }


    [RelayCommand]
    private Task RefreshAsync() => RefreshCoreAsync(background: false);

    private async Task RefreshCoreAsync(bool background)
    {
        var ticket = background ? generation : ++generation;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent || (Social is null && Communities is null) || Access is null) { NeedAccount = true; return; }
        if (!background) { IsBusy = true; LoadingInbox = true; InboxLoadFailed = false; }
        try
        {
            var token = await Access(operation.Token);
            if (!operation.IsCurrent || ticket != generation) return;
            if (string.IsNullOrWhiteSpace(token)) { ClearVisibleAvatars(); NeedAccount = true; MyCode = ""; return; }
            SocialHomeResponse? home = null;
            var socialUnavailable = Social is null;
            if (Social is not null)
                try { home = await Social.HomeAsync(token, operation.Token); }
                catch (SocialClientException) { socialUnavailable = true; }
            var rows = new List<ChatInboxRow>();
            var groupsUnavailable = false;
            if (Communities is not null)
                try
                {
                    var groups = (await Communities.ListAsync(token, ct: operation.Token)).Where(g => g.Role is not null).ToArray();
                    foreach (var group in groups)
                    {
                        try
                        {
                            var detail = await Communities.GroupHomeAsync(token, group.CommunityId, operation.Token);
                            var chat = detail.GroupChat;
                            rows.Add(new(chat.ConversationId, group.CommunityId, false, detail.GroupName ?? group.Name,
                                chat.LastBody ?? "Пока нет сообщений", chat.LastAt, chat.Unread,
                                new RelayCommand(() => OpenGroup(group.CommunityId)), avatarId: group.CommunityId, groupAvatar: true));
                            foreach (var direct in detail.Directs)
                                rows.Add(new(direct.ConversationId, group.CommunityId, false,
                                    $"{detail.GroupName ?? group.Name} · {direct.Title}",
                                    direct.LastBody ?? "Пока нет сообщений", direct.LastAt, direct.Unread,
                                    new RelayCommand(() => OpenGroupDirect(group.CommunityId, direct.ConversationId)),
                                    "Личный в группе", direct.PeerUserId));
                        }
                        catch (CommunityClientException) { groupsUnavailable = true; }
                    }
                }
                catch (CommunityClientException) { groupsUnavailable = true; }
            foreach (var friend in home?.Friends ?? [])
                rows.Add(new(friend.ConversationId, null, true, friend.DisplayName ?? friend.Username,
                    friend.LastBody ?? "Пока нет сообщений", friend.LastAt, friend.Unread,
                    new RelayCommand(() => _ = OpenPersonalAsync(friend)), avatarId: friend.UserId));
            if (!operation.IsCurrent || ticket != generation) return;
            if (groupsUnavailable)
                foreach (var previous in Chats.Where(row => !row.Personal && rows.All(next => next.ConversationId != row.ConversationId)))
                    rows.Add(previous);
            if (socialUnavailable)
                foreach (var previous in Chats.Where(row => row.Personal)) rows.Add(previous);
            NeedAccount = false;
            InboxLoadFailed = groupsUnavailable || socialUnavailable;
            InboxLoaded = true;
            if (home is not null) MyCode = home.Code;
            if (home is not null && peerId is Guid selectedPeer
                && home.Friends.FirstOrDefault(friend => friend.UserId == selectedPeer) is { } selectedFriend)
            {
                var currentTitle = selectedFriend.DisplayName ?? selectedFriend.Username;
                if (ChatTitle != currentTitle) ChatTitle = currentTitle;
                if (DateTimeOffset.UtcNow - lastChatIdentityCheck >= TimeSpan.FromMinutes(2))
                {
                    lastChatIdentityCheck = DateTimeOffset.UtcNow;
                    _ = LoadChatAvatarAsync(selectedPeer, ticket);
                }
            }
            var oldImages = Chats.Where(old => !rows.Contains(old)).Select(row => row.Avatar)
                .OfType<Bitmap>().Distinct<Bitmap>(ReferenceEqualityComparer.Instance).ToArray();
            Chats.Clear();
            foreach (var row in rows.OrderByDescending(r => r.LastAt)) Chats.Add(row);
            foreach (var old in oldImages) AvatarImages.Retire(old);
            _ = LoadInboxAvatarsAsync(rows, ticket);
            if (home is not null)
            {
                Incoming.Clear();
                foreach (var invite in home.Incoming)
                    Incoming.Add(new ChatInviteRow(invite.FriendshipId, invite.DisplayName ?? invite.Username,
                        (id, accept) => ResolveInviteAsync(id, accept)) { Busy = resolvingInvites.Contains(invite.FriendshipId) });
            }
            Status = (groupsUnavailable, socialUnavailable) switch
            {
                (true, true) => "Групповые и личные чаты временно недоступны.",
                (true, false) => "Групповые чаты временно недоступны.",
                (false, true) => "Личные чаты временно недоступны.",
                _ => ""
            };
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or CommunityClientException or AccountClientException)
        { if (operation.IsCurrent && ticket == generation) { InboxLoadFailed = true; Status = "Не удалось загрузить чаты."; } }
        finally { if (!background && operation.IsCurrent) { IsBusy = false; LoadingInbox = false; } }
    }

    private void OpenGroup(Guid communityId)
    {
        var group = shell.Section<Vograph.Desktop.Features.Groups.GroupViewModel>(SectionKey.Group);
        group.RequestCommunity(communityId);
        shell.NavigateTo(SectionKey.Group);
    }

    private void OpenGroupDirect(Guid communityId, Guid conversationId)
    {
        var group = shell.Section<Vograph.Desktop.Features.Groups.GroupViewModel>(SectionKey.Group);
        group.RequestConversation(communityId, conversationId);
        shell.NavigateTo(SectionKey.Group);
    }

    private async Task OpenPersonalAsync(SocialFriendResponse friend)
    {
        var ticket = ++generation;
        await CancelRecordingAsync();
        if (ticket != generation) return;
        StopPlayback();
        ReleaseConversationAvatar();
        Messages.Clear();
        MessageSearch = "";
        QuoteFeedback = "";
        ClearPreviews();
        SaveComposer();
        conversationId = friend.ConversationId;
        unreadAtOpen = friend.Unread; unreadSnapshotLastAt = friend.LastAt;
        UnreadJumpFeedback = ""; OnPropertyChanged(nameof(HasUnreadJump));
        StartRecordingCommand.NotifyCanExecuteChanged();
        peerId = friend.UserId;
        ChatTitle = friend.DisplayName ?? friend.Username;
        lastChatIdentityCheck = DateTimeOffset.UtcNow;
        _ = LoadChatAvatarAsync(friend.UserId, ticket);
        RestoreComposer(friend.ConversationId);
        MessagesLoaded = false;
        MessageLoadFailed = false;
        HasMore = false;
        OnPropertyChanged(nameof(HasConversation));
        OnPropertyChanged(nameof(NoConversation));
        await LoadMessagesAsync(ticket);
    }

    private async Task LoadMessagesAsync(int ticket, Guid? before = null)
    {
        if (conversationId is not Guid id || Social is null || Access is null) return;
        using var operation = App.Work.Enter();
        var request = ++messageLoadSerial;
        var historyVersion = AdvanceMessageHistory(id);
        LoadingMessages = true;
        MessageLoadFailed = false;
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent || ticket != generation) return;
            var page = await Social.MessagesAsync(token, id, before, operation.Token);
            if (!operation.IsCurrent || ticket != generation || !CurrentMessageHistory(id, historyVersion)) return;
            if (before is null) Messages.Clear();
            var olderIndex = 0;
            foreach (var message in page.Messages)
            {
                if (Messages.Any(row => row.Id == message.MessageId)) continue;
                if (before is null) Messages.Add(Row(message));
                else Messages.Insert(olderIndex++, Row(message));
            }
            HasMore = page.HasMore;
            MessagesLoaded = true;
            MarkCurrentChatRead(id);
            Status = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException)
        { if (operation.IsCurrent && ticket == generation && CurrentMessageHistory(id, historyVersion)) { MessageLoadFailed = true; Status = "Не удалось загрузить сообщения."; } }
        finally { if (operation.IsCurrent && request == messageLoadSerial) LoadingMessages = false; }
    }

    private async Task PullLatestAsync()
    {
        if (!watching || conversationId is not Guid id || Social is null || Access is null) return;
        var ticket = generation;
        var historyVersion = AdvanceMessageHistory(id);
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent || !watching || ticket != generation || conversationId != id) return;
            var page = await Social.MessagesAsync(token, id, ct: operation.Token);
            if (!operation.IsCurrent || !watching || ticket != generation || conversationId != id || !CurrentMessageHistory(id, historyVersion)) return;
            var fresh = page.Messages.ToList();
            var known = Messages.Select(row => row.Id).ToHashSet();
            var cursors = new HashSet<Guid>();
            while (known.Count > 0 && page.HasMore && fresh.All(message => !known.Contains(message.MessageId)))
            {
                if (page.Messages.Count == 0 || !cursors.Add(page.Messages[0].MessageId))
                    throw new SocialClientException(0);
                page = await Social.MessagesAsync(token, id, page.Messages[0].MessageId, operation.Token);
                if (!operation.IsCurrent || !watching || ticket != generation || conversationId != id || !CurrentMessageHistory(id, historyVersion)) return;
                fresh.InsertRange(0, page.Messages);
            }
            for (var position = 0; position < fresh.Count; position++)
            {
                var message = fresh[position];
                var found = Messages.ToList().FindIndex(row => row.Id == message.MessageId);
                if (found >= 0) { Messages[found] = Row(message); continue; }
                var next = fresh.Skip(position + 1).FirstOrDefault(candidate => Messages.Any(row => row.Id == candidate.MessageId));
                var insertAt = next is null ? Messages.Count : Messages.ToList().FindIndex(row => row.Id == next.MessageId);
                Messages.Insert(insertAt, Row(message));
            }
            if (known.Count == 0) HasMore = page.HasMore;
            MessagesLoaded = true;
            MessageLoadFailed = false;
            MarkCurrentChatRead(id);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException)
        { if (operation.IsCurrent && watching && ticket == generation && CurrentMessageHistory(id, historyVersion)) Status = "Не удалось обновить сообщения."; }
    }

    [RelayCommand]
    private Task LoadOlderAsync() => HasMore && Messages.Count > 0
        ? LoadMessagesAsync(generation, Messages[0].Id) : Task.CompletedTask;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SearchOlder()
    {
        if (!CanSearchOlder || conversationId is not Guid id) return;
        var query = MessageSearch.Trim();
        var ticket = generation;
        for (var page = 0; page < 5 && HasMore && Messages.Count > 0; page++)
        {
            if (conversationId != id || generation != ticket || MessageSearch.Trim() != query) return;
            var before = Messages.Count;
            await LoadMessagesAsync(ticket, Messages[0].Id);
            if (conversationId != id || generation != ticket || MessageSearch.Trim() != query) return;
            if (VisibleMessages.Count > 0)
            { HistorySearchFeedback = $"Найдено среди {Messages.Count} загруженных сообщений."; return; }
            if (Messages.Count == before)
            { HistorySearchFeedback = "Не удалось загрузить ранние сообщения. Повторите поиск."; return; }
        }
        HistorySearchFeedback = HasMore
            ? "Совпадений пока нет. Можно продолжить поиск в ранних сообщениях."
            : "Совпадений в доступной истории нет.";
    }

    [RelayCommand]
    private Task ReloadMessagesAsync() => LoadMessagesAsync(generation);

    [RelayCommand]
    private async Task InviteAsync()
    {
        if (Social is null || Access is null || string.IsNullOrWhiteSpace(InviteCode)) return;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent) return;
            await Social.InviteAsync(token, InviteCode.Trim(), operation.Token);
            if (!operation.IsCurrent) return;
            InviteCode = "";
            await RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException)
        { if (operation.IsCurrent) Status = "Не удалось отправить приглашение."; }
    }

    private async Task ResolveInviteAsync(Guid friendshipId, bool accept)
    {
        if (Social is null || Access is null || !resolvingInvites.Add(friendshipId)) return;
        var row = Incoming.FirstOrDefault(item => item.Id == friendshipId);
        if (row is not null) row.Busy = true;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent) return;
            if (accept) await Social.AcceptAsync(token, friendshipId, operation.Token);
            else await Social.DeclineAsync(token, friendshipId, operation.Token);
            if (operation.IsCurrent) await RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException)
        { if (operation.IsCurrent) Status = accept ? "Не удалось принять приглашение." : "Не удалось отклонить приглашение."; }
        finally
        {
            resolvingInvites.Remove(friendshipId);
            if (row is not null) row.Busy = false;
            foreach (var current in Incoming.Where(item => item.Id == friendshipId)) current.Busy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (!CanSend() || conversationId is not Guid id || Social is null || Access is null || !sendingConversations.Add(id)) return;
        SaveComposer();
        var submitted = CurrentComposer();
        Sending = true;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent) return;
            var response = submitted.Edit is Guid messageId
                ? await Social.EditAsync(token, id, messageId, NormalizeBody(submitted.Text), operation.Token)
                : await Social.SendTextAsync(token, id, NormalizeBody(submitted.Text), submitted.Reply, operation.Token);
            if (!operation.IsCurrent) return;
            AdvanceMessageHistory(id);
            if (composers.TryGetValue(id, out var current) && current.Revision == submitted.Revision && current == submitted)
            {
                composers[id] = new(submitted.Edit is not null ? submitted.Ordinary : "", submitted.Edit is not null ? submitted.OrdinaryReply : null,
                    null, "", null, submitted.Revision + 1, "");
                if (conversationId == id) RestoreComposer(id);
            }
            if (conversationId == id)
            {
                var previous = Messages.ToList().FindIndex(row => row.Id == response.MessageId);
                if (previous >= 0) Messages[previous] = Row(response); else Messages.Add(Row(response));
                ComposerError = ""; SaveComposer();
            }
            await RefreshCoreAsync(background: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException)
        {
            if (operation.IsCurrent && composers.TryGetValue(id, out var current))
            {
                composers[id] = current with { Error = "Не удалось отправить сообщение. Текст сохранён; повторите отправку." };
                if (conversationId == id) ComposerError = composers[id].Error;
            }
        }
        finally
        {
            sendingConversations.Remove(id);
            if (conversationId == id) Sending = false;
        }
    }

    private bool CanStartRecording(string? kind) => (kind is "voice" or "circle")
        && !Sending && !IsRecording && !IsFinalizingRecording && editing is null && conversationId is not null;

    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private async Task StartRecordingAsync(string? kind)
    {
        if (kind is not ("voice" or "circle") || !CanStartRecording(kind)) return;
        var ticket = generation;
        using var operation = App.Work.Enter();
        try
        {
            await recorder.StartAsync(kind, operation.Token);
            if (!operation.IsCurrent || ticket != generation)
            {
                await recorder.CancelAsync();
                return;
            }
            recordingKind = kind;
            recordingStarted = DateTimeOffset.UtcNow;
            RecordingCaption = kind == "voice" ? "Голосовое · 0:00" : "Кружок · 0:00";
            IsRecording = true;
            recordingTimer?.Stop();
            recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            recordingTimer.Tick += OnRecordingTick;
            recordingTimer.Start();
            Status = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or IOException or PlatformNotSupportedException or System.Runtime.InteropServices.COMException)
        { if (operation.IsCurrent && ticket == generation) Status = "Не удалось начать запись. Проверьте доступ к микрофону и камере."; }
    }

    private async void OnRecordingTick(object? sender, EventArgs e)
    {
        if (!IsRecording || recordingKind is null) return;
        var elapsed = DateTimeOffset.UtcNow - recordingStarted;
        RecordingCaption = (recordingKind == "voice" ? "Голосовое · " : "Кружок · ") +
            $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        if (recorder.CurrentLength > ChatMediaLimits.MaxBytes(recordingKind))
        {
            await CancelRecordingAsync();
            Status = "Запись слишком большая. Попробуйте короче.";
        }
        else if (elapsed.TotalMilliseconds >= ChatMediaLimits.MaxDurationMs(recordingKind) - 1000)
            await FinishRecordingAsync();
    }

    [RelayCommand]
    private async Task FinishRecordingAsync()
    {
        if (!IsRecording || IsFinalizingRecording) return;
        if (conversationId is not Guid id || Social is null || Access is null)
        {
            await CancelRecordingAsync();
            return;
        }
        IsFinalizingRecording = true;
        recordingTimer?.Stop();
        recordingTimer = null;
        IsRecording = false;
        recordingKind = null;
        RecordingCaption = "";
        var ticket = generation;
        ChatCapturedMedia? media = null;
        using var operation = App.Work.Enter();
        try
        {
            media = await recorder.FinishAsync(operation.Token);
            ChatMediaLimits.Validate(media);
            if (!operation.IsCurrent || ticket != generation || conversationId != id) return;
            var token = await Access(operation.Token);
            if (string.IsNullOrWhiteSpace(token) || !operation.IsCurrent || ticket != generation) return;
            var submittedReply = replyTo;
            var message = await Social.SendMediaAsync(token, id, media.Kind, media.FileName, media.Bytes,
                submittedReply, operation.Token, media.DurationMs);
            if (operation.IsCurrent) AdvanceMessageHistory(id);
            if (!operation.IsCurrent || ticket != generation || conversationId != id) return;
            Messages.Add(Row(message));
            if (submittedReply is not null && replyTo == submittedReply) { replyTo = null; ActionCaption = ""; composerRevision++; SaveComposer(); NotifyComposer(); }
            Status = "";
            await RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException
            or SocialClientException or AccountClientException or System.Runtime.InteropServices.COMException)
        { if (operation.IsCurrent && ticket == generation) Status = "Не удалось отправить запись."; }
        finally
        {
            if (media is not null) CryptographicOperations.ZeroMemory(media.Bytes);
            try { await recorder.CancelAsync(); }
            catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException) { }
            finally { IsFinalizingRecording = false; }
        }
    }

    [RelayCommand]
    private async Task CancelRecordingAsync()
    {
        if (IsFinalizingRecording) return;
        recordingTimer?.Stop();
        recordingTimer = null;
        IsRecording = false;
        recordingKind = null;
        RecordingCaption = "";
        try { await recorder.CancelAsync(); }
        catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException) { }
    }

    [RelayCommand(CanExecute = nameof(CanAttach))]
    private async Task AttachAsync(string? kind)
    {
        if (!CanAttach(kind) || conversationId is not Guid id || Social is null || Access is null || !sendingConversations.Add(id)) return;
        var ticket = generation;
        SaveComposer();
        var submitted = CurrentComposer();
        Sending = true;
        byte[]? bytes = null;
        using var operation = App.Work.Enter();
        try
        {
            var path = await App.FileDialogs.OpenChatMediaAsync(kind!);
            if (string.IsNullOrWhiteSpace(path) || !operation.IsCurrent || ticket != generation || conversationId != id) return;
            var file = new FileInfo(path);
            var limit = kind == "image" ? 25L * 1024 * 1024 : 20L * 1024 * 1024;
            if (!file.Exists || file.Length is < 1 || file.Length > limit)
            {
                SetAttachmentError(id, "Файл слишком большой или недоступен. Текст этой беседы сохранён.");
                return;
            }
            bytes = await File.ReadAllBytesAsync(path, operation.Token);
            if (!operation.IsCurrent || ticket != generation || conversationId != id) return;
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent || ticket != generation) return;
            var message = await Social.SendMediaAsync(token, id, kind!, file.Name, bytes, submitted.Reply, operation.Token);
            if (operation.IsCurrent) AdvanceMessageHistory(id);
            if (!operation.IsCurrent || ticket != generation || conversationId != id) return;
            Messages.Add(Row(message));
            if (submitted.Reply is not null && composerRevision == submitted.Revision && replyTo == submitted.Reply)
            { replyTo = null; ActionCaption = ""; composerRevision++; SaveComposer(); NotifyComposer(); }
            if (composers.TryGetValue(id, out var afterSend)) composers[id] = afterSend with { Error = "" };
            ComposerError = "";
            await RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException or IOException or UnauthorizedAccessException)
        { if (operation.IsCurrent) SetAttachmentError(id, "Не удалось отправить файл. Текст этой беседы сохранён; выберите файл ещё раз."); }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            sendingConversations.Remove(id);
            if (operation.IsCurrent && conversationId == id) Sending = false;
        }
    }

    private ChatMessageRow Row(SocialMessageResponse message)
    {
        var scope = conversationId;
        var row = new ChatMessageRow(message,
            message.SenderId != peerId,
            new RelayCommand(() => BeginReply(message.MessageId)),
            new RelayCommand(() => BeginEdit(message.MessageId, message.Body ?? "")),
            new AsyncRelayCommand(() => ChangeMessageAsync(message.MessageId, true)),
            new AsyncRelayCommand(() => ChangeMessageAsync(message.MessageId, false)),
            message.AttachmentId is Guid && !message.Deleted && message.Kind is "image" or "file" or "voice" or "circle"
                ? new AsyncRelayCommand(() => SaveAttachmentAsync(message)) : null,
            message.AttachmentId is Guid && !message.Deleted && message.Kind is "voice" or "circle"
                ? new AsyncRelayCommand(() => PlayMediaAsync(message)) : null,
            new AsyncRelayCommand(() => CopyPersonalMessageAsync(message, scope)));
        if (message.Kind == "image" && !message.Deleted && message.AttachmentId is Guid attachment)
        {
            if (previewCache.TryGetValue(attachment, out var cached)) row.Preview = cached;
            else Dispatcher.UIThread.Post(() => _ = LoadPhotoPreviewAsync(attachment));
        }
        return row;
    }

    private void SetAttachmentError(Guid id, string message)
    {
        if (composers.TryGetValue(id, out var current)) composers[id] = current with { Error = message };
        if (conversationId == id) ComposerError = message;
    }

    private async Task CopyPersonalMessageAsync(SocialMessageResponse message, Guid? scope)
    {
        var content = PersonalMessageText.CopyText(message);
        if (scope is null || conversationId != scope || peerId is null || content is null
            || !Messages.Any(row => row.Id == message.MessageId && row.CanCopy)) return;
        try
        {
            if (clipboardWriter is null) throw new InvalidOperationException("Clipboard unavailable");
            await clipboardWriter(content);
            if (conversationId == scope) Status = "Текст скопирован.";
        }
        catch { if (conversationId == scope) Status = "Не удалось скопировать текст."; }
    }

    private void GroupMessages()
    {
        ChatMessageRow? previous = null;
        foreach (var row in Messages)
        {
            var day = row.CreatedAt.ToLocalTime().Date;
            var newDay = previous is null || previous.CreatedAt.ToLocalTime().Date != day;
            row.DayHeader = newDay ? day.ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) : "";
            row.ShowAuthor = !row.Mine && (previous is null || newDay || previous.Mine);
            row.Avatar = !row.Mine ? ChatAvatar : null;
            previous = row;
        }
    }

    private void MarkCurrentChatRead(Guid id)
    {
        foreach (var row in Chats.Where(row => row.ConversationId == id)) row.MarkRead();
        RefreshInboxBrowse();
    }

    private async Task LoadInboxAvatarsAsync(IReadOnlyList<ChatInboxRow> rows, int ticket)
    {
        if (App.Avatars is null || Access is null) return;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrWhiteSpace(token) || !operation.IsCurrent || ticket != generation) return;
            foreach (var row in rows.Where(row => row.AvatarId is not null))
            {
                var image = row.GroupAvatar
                    ? await App.Avatars.GroupAsync(token, row.AvatarId!.Value, operation.Token)
                    : await App.Avatars.UserAsync(token, row.AvatarId!.Value, operation.Token);
                if (!operation.IsCurrent || ticket != generation || !Chats.Contains(row)) { image?.Dispose(); return; }
                var old = row.Avatar;
                row.Avatar = image;
                if (!ReferenceEquals(old, image)) AvatarImages.Retire(old);
            }
        }
        catch (AvatarClientException ex) when (ex.Status is 401 or 403) { ClearVisibleAvatars(); }
        catch (Exception ex) when (ex is AvatarClientException or AccountClientException or InvalidDataException or OperationCanceledException) { }
    }

    private async Task LoadChatAvatarAsync(Guid userId, int ticket)
    {
        if (App.Avatars is null || Access is null) return;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrWhiteSpace(token) || !operation.IsCurrent || ticket != generation) return;
            var image = await App.Avatars.UserAsync(token, userId, operation.Token);
            if (operation.IsCurrent && ticket == generation && peerId == userId)
            {
                var old = ChatAvatar;
                ChatAvatar = image;
                GroupMessages();
                if (!ReferenceEquals(old, image)) AvatarImages.Retire(old);
            }
            else image?.Dispose();
        }
        catch (AvatarClientException ex) when (ex.Status is 401 or 403) { ClearVisibleAvatars(); }
        catch (Exception ex) when (ex is AvatarClientException or AccountClientException or InvalidDataException or OperationCanceledException) { }
    }

    private void ClearVisibleAvatars()
    {
        App.Avatars?.Clear();
        ReleaseVisibleAvatars();
    }

    private void ReleaseVisibleAvatars()
    {
        var images = new[] { ChatAvatar }
            .Concat(Chats.Select(row => row.Avatar))
            .Concat(Messages.Select(row => row.Avatar))
            .OfType<Bitmap>().Distinct<Bitmap>(ReferenceEqualityComparer.Instance).ToArray();
        ChatAvatar = null;
        foreach (var row in Chats) row.Avatar = null;
        foreach (var row in Messages) row.Avatar = null;
        foreach (var image in images) AvatarImages.Retire(image);
    }

    private void ReleaseConversationAvatar()
    {
        var images = new[] { ChatAvatar }.Concat(Messages.Select(row => row.Avatar))
            .OfType<Bitmap>().Distinct<Bitmap>(ReferenceEqualityComparer.Instance).ToArray();
        ChatAvatar = null;
        foreach (var row in Messages) row.Avatar = null;
        foreach (var image in images) AvatarImages.Retire(image);
    }

    private async Task LoadPhotoPreviewAsync(Guid attachmentId)
    {
        if (!previewLoading.Add(attachmentId) || Social is null || Access is null) return;
        var selectedConversation = conversationId;
        var cancellation = previewCancellation.Token;
        byte[]? bytes = null;
        Bitmap? bitmap = null;
        try
        {
            var token = await Access(cancellation);
            if (string.IsNullOrWhiteSpace(token) || cancellation.IsCancellationRequested || selectedConversation != conversationId) return;
            bytes = await Social.ReadAttachmentAsync(token, attachmentId, cancellation);
            if (cancellation.IsCancellationRequested || selectedConversation != conversationId) return;
            bitmap = ChatMediaPreview.DecodePhoto(bytes);
            previewCache[attachmentId] = bitmap;
            foreach (var row in Messages.Where(row => row.AttachmentId == attachmentId)) row.Preview = bitmap;
            bitmap = null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocialClientException or AccountClientException
            or ArgumentException or InvalidDataException or IOException) { }
        finally
        {
            bitmap?.Dispose();
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            previewLoading.Remove(attachmentId);
        }
    }

    private void ClearPreviews()
    {
        previewCancellation.Cancel();
        previewCancellation.Dispose();
        previewCancellation = new CancellationTokenSource();
        previewLoading.Clear();
        foreach (var image in previewCache.Values) image.Dispose();
        previewCache.Clear();
    }

    private async Task PlayMediaAsync(SocialMessageResponse message)
    {
        if (message.AttachmentId is not Guid attachment || conversationId is not Guid id
            || Social is null || Access is null) return;
        if (playingMessage == message.MessageId) { StopPlayback(); return; }
        StopPlayback();
        var ticket = generation;
        byte[]? bytes = null;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrWhiteSpace(token) || !operation.IsCurrent || ticket != generation) return;
            bytes = await Social.ReadAttachmentAsync(token, attachment, operation.Token);
            if (!operation.IsCurrent || ticket != generation || conversationId != id) return;
            await player.PlayAsync(message.Kind, message.ContentType, bytes, operation.Token);
            if (!operation.IsCurrent || ticket != generation || conversationId != id)
            {
                player.Stop();
                return;
            }
            if (message.Kind == "voice")
            {
                playingMessage = message.MessageId;
                foreach (var row in Messages.Where(row => row.Id == message.MessageId)) row.IsPlaying = true;
            }
            Status = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException or IOException or InvalidDataException
            or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { if (operation.IsCurrent && ticket == generation) Status = "Не удалось воспроизвести запись."; }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }

    private void StopPlayback()
    {
        player.Stop();
        playingMessage = null;
        foreach (var row in Messages) row.IsPlaying = false;
    }

    private async Task SaveAttachmentAsync(SocialMessageResponse message)
    {
        if (message.AttachmentId is not Guid attachmentId || conversationId is not Guid id
            || Social is null || Access is null || Messages.All(row => row.Id != message.MessageId)) return;
        var ticket = generation;
        var suggested = SafeAttachmentName(message.FileName, message.Kind);
        var path = await App.FileDialogs.SaveChatMediaAsync(suggested);
        if (string.IsNullOrWhiteSpace(path) || ticket != generation || conversationId != id) return;
        if (File.Exists(path)) { Status = "Файл уже существует. Выберите новое имя."; return; }
        byte[]? bytes = null;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent || ticket != generation) return;
            bytes = await Social.ReadAttachmentAsync(token, attachmentId, operation.Token);
            if (!operation.IsCurrent || ticket != generation || conversationId != id) return;
            await File.WriteAllBytesAsync(temporary, bytes, operation.Token);
            if (!operation.IsCurrent || ticket != generation || conversationId != id) return;
            File.Move(temporary, path);
            Status = "Файл сохранён.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException or IOException or UnauthorizedAccessException)
        { if (operation.IsCurrent && ticket == generation) Status = "Не удалось сохранить файл."; }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string SafeAttachmentName(string? value, string kind)
    {
        var name = (value ?? "").Replace('\\', '/').Split('/').LastOrDefault() ?? "";
        var clean = new string(name.Where(ch => !char.IsControl(ch) && ch is not ('<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')).ToArray()).Trim();
        if (clean is "" or "." or "..") clean = kind switch
        {
            "image" => "Фото.webp", "voice" => "Голосовое.m4a", "circle" => "Кружок.mp4", _ => "Документ"
        };
        return clean.Length <= 120 ? clean : clean[..120];
    }

    private async Task ChangeMessageAsync(Guid messageId, bool delete)
    {
        if (conversationId is not Guid id || Social is null || Access is null) return;
        var ticket = generation;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent || ticket != generation) return;
            var changed = delete ? await Social.DeleteAsync(token, id, messageId, operation.Token)
                : await Social.ReactAsync(token, id, messageId, "like", operation.Token);
            if (operation.IsCurrent) AdvanceMessageHistory(id);
            if (!operation.IsCurrent || ticket != generation) return;
            var index = Messages.ToList().FindIndex(row => row.Id == messageId);
            if (index >= 0) Messages[index] = Row(changed);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException)
        { if (operation.IsCurrent && ticket == generation) Status = "Не удалось изменить сообщение."; }
    }
}

public sealed partial class ChatInboxRow(Guid conversationId, Guid? communityId, bool personal, string title,
    string preview, DateTimeOffset? lastAt, int unread, IRelayCommand open, string? kind = null,
    Guid? avatarId = null, bool groupAvatar = false) : ObservableObject
{
    public Guid? AvatarId { get; } = avatarId;
    public bool GroupAvatar { get; } = groupAvatar;
    public string Initials => AvatarInitials.FromName(Title);
    [ObservableProperty] private Bitmap? avatar;
    public bool HasAvatar => Avatar is not null;
    public bool NoAvatar => Avatar is null;
    partial void OnAvatarChanged(Bitmap? value)
    { OnPropertyChanged(nameof(HasAvatar)); OnPropertyChanged(nameof(NoAvatar)); }
    public Guid ConversationId { get; } = conversationId;
    public Guid? CommunityId { get; } = communityId;
    public bool Personal { get; } = personal;
    public string Kind => kind ?? (Personal ? "Личный" : "Группа");
    public int SourceIndex => Personal ? 3 : kind == "Личный в группе" ? 2 : 1;
    public string SourceTitle => SourceIndex switch { 1 => "Группа", 2 => "Личный чат группы", _ => "Друг по коду" };
    public string Title { get; } = title;
    public string Preview { get; } = preview;
    public DateTimeOffset? LastAt { get; } = lastAt;
    public string When
    {
        get
        {
            if (LastAt is not { } at) return "";
            var local = at.ToLocalTime();
            return local.ToString(local.Year == DateTime.Now.Year ? "dd.MM HH:mm" : "dd.MM.yyyy HH:mm");
        }
    }
    public string Unread => UnreadBadge.Label(UnreadCount);
    public int UnreadCount { get; private set; } = unread;
    public string UnreadDescription => UnreadBadge.Description(UnreadCount);
    internal void MarkRead()
    {
        if (UnreadCount == 0) return;
        UnreadCount = 0;
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(Unread));
        OnPropertyChanged(nameof(UnreadDescription));
    }
    public IRelayCommand OpenCommand { get; } = open;
}

public sealed partial class ChatInviteRow : ObservableObject
{
    public ChatInviteRow(Guid id, string name, Func<Guid, bool, Task> resolve)
    {
        Id = id; Name = name;
        AcceptCommand = new AsyncRelayCommand(() => resolve(id, true));
        DeclineCommand = new AsyncRelayCommand(() => resolve(id, false));
    }
    public Guid Id { get; }
    public string Name { get; }
    [ObservableProperty] private bool busy;
    public IAsyncRelayCommand AcceptCommand { get; }
    public IAsyncRelayCommand DeclineCommand { get; }
}

public sealed partial class ChatMessageRow(SocialMessageResponse response, bool mine, IRelayCommand reply,
    IRelayCommand edit, IAsyncRelayCommand delete, IAsyncRelayCommand react, IAsyncRelayCommand? download,
    IAsyncRelayCommand? play, IAsyncRelayCommand copy) : ObservableObject
{
    public Guid Id => response.MessageId;
    public Guid? ReplyToId => response.ReplyTo;
    public bool IsReply => ReplyToId is not null;
    public bool Deleted => response.Deleted;
    public bool IsIncoming => !Mine;
    [ObservableProperty] private bool isUnreadTarget;
    [ObservableProperty] private bool isQuoteTarget;
    [ObservableProperty] private string quoteHint = "";
    public DateTimeOffset CreatedAt => response.CreatedAt;
    [ObservableProperty] private string dayHeader = "";
    [ObservableProperty] private bool showAuthor;
    [ObservableProperty] private Bitmap? avatar;
    public bool HasAvatar => Avatar is not null;
    public bool NoAvatar => Avatar is null;
    partial void OnAvatarChanged(Bitmap? value)
    { OnPropertyChanged(nameof(HasAvatar)); OnPropertyChanged(nameof(NoAvatar)); }
    public string Initials => AvatarInitials.FromName(Author);
    public Guid? AttachmentId => response.AttachmentId;
    public string Author => response.SenderName;
    public string Display => response.Deleted ? "Сообщение удалено" : response.Kind switch
    {
        "image" => "Фото", "file" => response.FileName ?? "Документ",
        "voice" => "Голосовое" + Duration(), "circle" => "Кружок" + Duration(),
        _ => response.Body ?? response.Kind
    };
    private string Duration() => response.DurationMs is int ms
        ? " · " + TimeSpan.FromMilliseconds(ms).ToString(@"m\:ss") : "";
    public string Detail => response.ReplyBody is null ? "" : "Ответ: " + response.ReplyBody;
    public string When => response.CreatedAt.ToLocalTime().ToString("dd.MM HH:mm");
    public string ActionName => $"Действия сообщения {Author}, {When}";
    public bool Mine { get; } = mine;
    public bool CanEdit => Mine && !response.Deleted && response.Kind == "text";
    public bool CanDelete => Mine && !response.Deleted;
    public bool CanReact => !response.Deleted;
    public bool CanCopy => PersonalMessageText.CopyText(response) is not null;
    public string SearchableText => PersonalMessageText.SearchText(response);
    public IAsyncRelayCommand CopyCommand { get; } = copy;
    public string Reactions => string.Join(" ", response.Reactions.Select(r => $"{r.Emoji} {r.Count}"));
    public IRelayCommand ReplyCommand { get; } = reply;
    public IRelayCommand EditCommand { get; } = edit;
    public IAsyncRelayCommand DeleteCommand { get; } = delete;
    public IAsyncRelayCommand ReactCommand { get; } = react;
    public bool CanDownload => DownloadCommand is not null;
    public IAsyncRelayCommand? DownloadCommand { get; } = download;
    public bool CanPlay => PlayCommand is not null;
    public IAsyncRelayCommand? PlayCommand { get; } = play;
    public string PlayCaption => response.Kind == "circle" ? "Смотреть кружок" : IsPlaying ? "Остановить" : "Слушать";
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private Bitmap? preview;
    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayCaption));
}
