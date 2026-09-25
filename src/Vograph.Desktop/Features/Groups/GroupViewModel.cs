using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Features.Chat;
using Vograph.Desktop.Services;
using Vograph.Desktop.ViewModels;
using Zapara.Client.Domain;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel : ViewModelBase
{
    private readonly CommunityHttpClient? client;
    private readonly Func<CancellationToken, Task<string?>>? accessToken;
    private readonly IChatMediaRecorder recorder;
    private Func<string, Task>? clipboardWriter;
    private readonly ChatMediaPlayer player = new();
    private readonly Dictionary<Guid, Bitmap> previewCache = [];
    private readonly HashSet<Guid> previewLoading = [];
    private CancellationTokenSource previewCancellation = new();
    private DispatcherTimer? recordingTimer;
    private DateTimeOffset recordingStarted;
    private string? recordingKind;
    private Guid? playingMessage;
    // Built before UseCommunities: keep reading the live session instead of the null captured here.
    private CommunityHttpClient? Api => client ?? App.Communities;
    private Func<CancellationToken, Task<string?>>? Access => accessToken ?? App.CommunityAccess;
    private int polling;
    private Guid me;
    private Guid? conversationId;
    private Guid? communityId;
    private Guid? requestedCommunityId;
    private (Guid CommunityId, Guid ConversationId)? requestedConversation;
    private int navigationGeneration;
    private int busyDepth;
    private bool watching;
    private DispatcherTimer? timer;

    public GroupViewModel(AppServices app, IChatMediaRecorder? recorder = null,
        Func<string, Task>? clipboardWriter = null) : base(app)
    {
        client = app.Communities;
        accessToken = app.CommunityAccess;
        this.recorder = recorder ?? new WindowsChatMediaRecorder();
        this.clipboardWriter = clipboardWriter;
        Messages.CollectionChanged += (_, _) => RefreshMessageBrowse();
        Ballots.CollectionChanged += (_, _) => RefreshBallotBrowse();
        player.PlaybackEnded += () => Dispatcher.UIThread.Post(StopPlayback);
        player.PlaybackFailed += () => Dispatcher.UIThread.Post(() =>
        {
            if (playingMessage is null) return;
            StopPlayback();
            Status = "Не удалось воспроизвести запись.";
        });
        NeedAccount = Api is null || Access is null;
    }

    public string Title => T("groupTitle");
    public ObservableCollection<GroupCommunityRow> Communities { get; } = [];
    public ObservableCollection<GroupPersonRow> People { get; } = [];
    public ObservableCollection<GroupPersonRow> Directs { get; } = [];
    public ObservableCollection<GroupMessageRow> Messages { get; } = [];

    [ObservableProperty] private bool needAccount;
    [ObservableProperty] private bool isEmpty;
    [ObservableProperty] private bool hasHome;
    [ObservableProperty] private bool hasMore;
    [ObservableProperty] private bool isDirect;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string homeTitle = "";
    [ObservableProperty] private string chatTitle = "";
    [ObservableProperty] private string draft = "";
    [ObservableProperty] private string holdCaption = "";
    [ObservableProperty] private bool isRecording;
    [ObservableProperty] private bool isFinalizingRecording;
    [ObservableProperty] private string recordingCaption = "";
    private Guid? replyTo;
    private Guid? editing;
    private string? pendingKind;
    private string? pendingName;
    private byte[]? pendingBytes;
    public bool ShowList => !NeedAccount && !HasHome && !IsEmpty;
    public bool HasDirects => Directs.Count > 0;
    public bool CanAttachMedia => ShowComposer && !IsBusy && !IsRecording && !IsFinalizingRecording;
    public void SetClipboardWriter(Func<string, Task>? writer) => clipboardWriter = writer;

    public override void Detach() => Watch(false);
    public override Task ActivateAsync() => LoadAsync();
    public void Watch(bool visible)
    {
        watching = visible;
        RestartTimer();
        if (!visible)
        {
            _ = CancelRecordingAsync();
            StopPlayback();
        }
    }

    partial void OnNeedAccountChanged(bool value) => RaiseList();
    partial void OnIsEmptyChanged(bool value) => RaiseList();
    partial void OnHasHomeChanged(bool value) => RaiseList();
    partial void OnIsRecordingChanged(bool value)
    {
        SendCommand.NotifyCanExecuteChanged();
        StartRecordingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanAttachMedia));
    }
    partial void OnIsFinalizingRecordingChanged(bool value)
    {
        SendCommand.NotifyCanExecuteChanged();
        StartRecordingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanAttachMedia));
    }
    partial void OnDraftChanged(string value)
    {
        if (conversationId is Guid id && editing is null && replyTo is null)
        {
            var key = DraftKey(id);
            if (value.Length == 0) channelDrafts.Remove(key);
            else channelDrafts[key] = value;
        }
        SendCommand.NotifyCanExecuteChanged();
    }
    private void Busy(bool value)
    {
        busyDepth = Math.Max(0, busyDepth + (value ? 1 : -1));
        IsBusy = busyDepth != 0;
        SendCommand.NotifyCanExecuteChanged();
        StartRecordingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanAttachMedia));
    }

    private async Task LoadAsync()
    {
        var ticket = ++navigationGeneration;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent || Api is null || Access is null)
        {
            ShowAccount();
            return;
        }
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (!operation.IsCurrent || ticket != navigationGeneration) return;
            if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
            NeedAccount = false;
            var wanted = await RunAsync(() =>
            {
                var id = App.Db.GetSettings().MyGroupId;
                return string.IsNullOrEmpty(id) ? "" : App.Db.GetGroup(id)?.Name ?? "";
            }, "моя группа") ?? "";
            var rows = (await Api.ListAsync(token, ct: operation.Token)).Where(item => item.Role is not null).ToArray();
            if (!operation.IsCurrent || ticket != navigationGeneration) return;
            Communities.Clear();
            foreach (var row in rows)
                Communities.Add(new(row.Name, Role(row.Role), new RelayCommand(() => _ = OpenAsync(row.CommunityId))));
            RefreshCommunityBrowse();
            var preferred = rows.FirstOrDefault(item => item.CommunityId == requestedConversation?.CommunityId)
                ?? rows.FirstOrDefault(item => item.CommunityId == requestedCommunityId)
                ?? rows.FirstOrDefault(item => item.Name == wanted || item.Name == "Группа " + wanted)
                ?? (rows.Length == 1 ? rows[0] : null);
            requestedCommunityId = null;
            IsEmpty = rows.Length == 0;
            HasHome = false;
            Status = "";
            RaiseList();
            if (preferred is not null) await OpenAsync(preferred.CommunityId);
        }
        catch (CommunityClientException) when (operation.IsCurrent && ticket == navigationGeneration) { Status = T("groupFailed"); }
        catch (AccountClientException ex) when (operation.IsCurrent && ticket == navigationGeneration) { FailSession(ex); }
        catch (OperationCanceledException) { }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    public void RequestCommunity(Guid id)
    {
        requestedCommunityId = id;
        requestedConversation = null;
    }

    public void RequestConversation(Guid communityId, Guid conversationId)
    {
        requestedCommunityId = communityId;
        requestedConversation = (communityId, conversationId);
    }

    private async Task OpenAsync(Guid id)
    {
        var ticket = ++navigationGeneration;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent || Api is null || Access is null) return;
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent || ticket != navigationGeneration) return;
            var home = await Api.GroupHomeAsync(token, id, operation.Token);
            if (!operation.IsCurrent || ticket != navigationGeneration) return;
            Show(home);
            await LoadChannelsAsync(token, id, home.GroupChat.ConversationId, ticket, operation.Token);
            if (!operation.IsCurrent || ticket != navigationGeneration) return;
            await LoadDeskAsync(token, id, home.Classmates, ticket, operation.Token);
            if (!operation.IsCurrent || ticket != navigationGeneration) return;
            if (requestedConversation is { } requested && requested.CommunityId == id)
            {
                requestedConversation = null;
                var direct = home.Directs.FirstOrDefault(chat => chat.ConversationId == requested.ConversationId);
                if (direct is null)
                {
                    Status = "Личная беседа группы больше недоступна.";
                    return;
                }
                await OpenConversationAsync(direct.ConversationId, direct.Title, true);
                return;
            }
            await OpenChannelAsync(Channels[0]);
        }
        catch (CommunityClientException) when (operation.IsCurrent && ticket == navigationGeneration) { Status = T("groupFailed"); }
        catch (AccountClientException ex) when (operation.IsCurrent && ticket == navigationGeneration) { FailSession(ex); }
        catch (OperationCanceledException) { }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    private void Show(GroupHomeResponse home)
    {
        communityId = home.CommunityId;
        me = home.Classmates.FirstOrDefault(person => person.Self)?.UserId ?? Guid.Empty;
        HomeTitle = home.GroupName ?? home.Name;
        HasHome = true;
        IsEmpty = false;
        MemberSearch = "";
        ChannelSearch = "";
        ChannelKindIndex = 0;
        UnreadOnly = false;
        ShowChannelManagement = false;
        People.Clear();
        foreach (var person in home.Classmates)
            People.Add(new(person.DisplayName ?? person.Username, "@" + person.Username, Role(person.Role), "", "", person.Self,
                person.Self ? null : new RelayCommand(() => _ = OpenDirectAsync(person.UserId, person.DisplayName ?? person.Username))));
        RefreshPeopleBrowse();
        Directs.Clear();
        foreach (var chat in home.Directs)
            Directs.Add(new(chat.Title, chat.LastBody ?? "", "", chat.LastBody ?? "", chat.Unread > 0 ? chat.Unread.ToString() : "", false,
                new RelayCommand(() => _ = OpenConversationAsync(chat.ConversationId, chat.Title, true))));
        OnPropertyChanged(nameof(HasDirects));
        RaiseList();
    }

    private async Task OpenDirectAsync(Guid userId, string title)
    {
        if (communityId is not Guid community || Api is null || Access is null) return;
        var ticket = ++navigationGeneration;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (ticket != navigationGeneration) return;
            if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
            var conversation = await Api.OpenDirectAsync(token, new(community, userId), operation.Token);
            var home = await Api.GroupHomeAsync(token, community, operation.Token);
            if (!operation.IsCurrent || ticket != navigationGeneration) return;
            Show(home);
            await OpenConversationAsync(conversation.ConversationId, title, true);
        }
        catch (CommunityClientException) when (operation.IsCurrent && ticket == navigationGeneration) { Status = T("groupFailed"); }
        catch (AccountClientException ex) when (operation.IsCurrent && ticket == navigationGeneration) { FailSession(ex); }
        catch (OperationCanceledException) { }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    private async Task OpenConversationAsync(Guid id, string title, bool direct, GroupChannelRow? channel = null)
    {
        var ticket = ++navigationGeneration;
        await CancelRecordingAsync();
        if (ticket != navigationGeneration) return;
        StopPlayback();
        Messages.Clear();
        ballotRequestSerial++;
        Ballots.Clear();
        BallotLoading = false;
        BallotLoadFailed = false;
        BallotLoaded = false;
        BallotFeedback = "";
        ResetBallotFilters();
        ShowBallotComposer = false;
        ResetMessageFilters();
        LoadingOlder = false;
        CancelDeleteMessage();
        ClearPreviews();
        conversationId = id;
        selectedGroupChannel = !direct;
        selectedTopicId = channel?.TopicId;
        ShowBallots = channel?.Kind == "ballots";
        SelectBallotDraft(channel);
        StartRecordingCommand.NotifyCanExecuteChanged();
        if (pendingBytes is not null) CryptographicOperations.ZeroMemory(pendingBytes);
        pendingBytes = null;
        pendingKind = null;
        pendingName = null;
        ChatTitle = title;
        IsDirect = direct;
        Draft = channelDrafts.GetValueOrDefault(DraftKey(id)) ?? "";
        replyTo = null;
        editing = null;
        HoldCaption = "";
        HasMore = false;
        try
        {
            if (ShowBallots) await LoadBallotsAsync(id, ticket);
            else await LoadLatestAsync(id, ticket);
            if (!CurrentChat(id, ticket)) return;
            if (direct && Api is not null && Access is not null)
            {
                try
                {
                    using var operation = App.Work.Enter();
                    var token = await Access(operation.Token);
                    if (string.IsNullOrEmpty(token)) { if (operation.IsCurrent && CurrentChat(id, ticket)) ShowAccount(); }
                    else if (operation.IsCurrent && CurrentChat(id, ticket)) await Api.MarkReadAsync(token, id, operation.Token);
                }
                catch (CommunityClientException) { }
                catch (AccountClientException ex) { if (CurrentChat(id, ticket)) FailSession(ex); }
            }
            if (CurrentChat(id, ticket)) RestartTimer();
        }
        catch (OperationCanceledException) { }
        catch (CommunityClientException) { if (CurrentChat(id, ticket)) Status = T("groupFailed"); }
        catch (AccountClientException ex) { if (CurrentChat(id, ticket)) FailSession(ex); }
    }

    private async Task LoadLatestAsync(Guid id, int ticket)
    {
        if (!CurrentChat(id, ticket) || Api is null || Access is null) return;
        using var operation = App.Work.Enter();
        var token = await Access(operation.Token);
        if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
        if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
        var page = await Api.MessagesAsync(token, id, ct: operation.Token, topic: TopicFilter);
        if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
        Messages.Clear();
        foreach (var message in page.Messages) Messages.Add(Row(message));
        HasMore = page.HasMore;
    }

    [RelayCommand]
    private async Task LoadOlder()
    {
        if (LoadingOlder || conversationId is not Guid id || Messages.Count == 0 || Api is null || Access is null) return;
        var ticket = navigationGeneration;
        using var operation = App.Work.Enter();
        LoadingOlder = true;
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
            if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
            var page = await Api.MessagesAsync(token, id, before: Messages[0].Id, ct: operation.Token, topic: TopicFilter);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
            HasMore = page.HasMore;
            for (var i = page.Messages.Count - 1; i >= 0; i--)
                if (Messages.All(item => item.Id != page.Messages[i].MessageId)) Messages.Insert(0, Row(page.Messages[i]));
        }
        catch (CommunityClientException) when (operation.IsCurrent && ticket == navigationGeneration) { Status = T("groupFailed"); }
        catch (AccountClientException ex) when (operation.IsCurrent && ticket == navigationGeneration) { FailSession(ex); }
        catch (OperationCanceledException) { }
        finally
        {
            if (ticket == navigationGeneration) LoadingOlder = false;
            if (operation.IsCurrent) Busy(false);
        }
    }

    private async Task PullAsync()
    {
        if (conversationId is not Guid id || Api is null || Access is null) return;
        var ticket = navigationGeneration;
        using var operation = App.Work.Enter();
        var token = await Access(operation.Token);
        if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
        if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
        var page = await Api.MessagesAsync(token, id, ct: operation.Token, topic: TopicFilter);
        if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
        if (Messages.Count == 0) HasMore = page.HasMore;
        var incoming = new List<ChatMessageResponse>();
        var reachedLatest = true;
        if (Messages.Count > 0 && page.Messages.Count > 0 && page.Messages.All(item => item.MessageId != Messages[^1].Id))
        {
            var cursor = Messages[^1].Id;
            var seen = new HashSet<Guid> { cursor };
            var caughtUp = false;
            for (var count = 0; count < 8; count++)
            {
                var next = await Api.MessagesAsync(token, id, after: cursor, ct: operation.Token, topic: TopicFilter);
                if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
                if (next.Messages.Count == 0) throw new CommunityClientException(CommunityClientFailure.InvalidPayload);
                incoming.AddRange(next.Messages);
                cursor = next.Messages[^1].MessageId;
                if (!seen.Add(cursor)) throw new CommunityClientException(CommunityClientFailure.InvalidPayload);
                if (!next.HasMore) { caughtUp = true; break; }
            }
            if (caughtUp) incoming.AddRange(page.Messages);
            else reachedLatest = false;
        }
        else incoming.AddRange(page.Messages);
        var receivedFromOther = false;
        foreach (var message in incoming)
        {
            var index = Messages.ToList().FindIndex(item => item.Id == message.MessageId);
            if (index < 0)
            {
                Messages.Add(Row(message));
                receivedFromOther |= message.SenderId != me;
            }
            else if (Messages[index].Body != message.Body || Messages[index].Deleted != message.Deleted
                || Messages[index].Kind != message.Kind || !Messages[index].ReactionSummaries.SequenceEqual(message.Reactions))
                Messages[index] = Row(message);
        }
        if (IsDirect && receivedFromOther && reachedLatest && operation.IsCurrent && CurrentChat(id, ticket))
            await Api.MarkReadAsync(token, id, operation.Token);
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        if (IsBusy) return;
        var api = Api;
        var file = pendingBytes;
        var fileKind = pendingKind;
        var fileName = pendingName;
        if (!ShowComposer || conversationId is not Guid id || api is null || Access is null || (string.IsNullOrWhiteSpace(Draft) && file is null)) return;
        var ticket = navigationGeneration;
        var draftKey = DraftKey(id);
        var sentTopic = selectedGroupChannel ? selectedTopicId : null;
        var sentGroupChannel = selectedGroupChannel;
        var submittedDraft = Draft;
        var submittedReply = replyTo;
        var submittedEdit = editing;
        using var operation = App.Work.Enter();
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (!CurrentChat(id, ticket)) return;
            if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
            ChatMessageResponse message;
            if (file is { Length: > 0 } && fileKind is "image" or "video" or "file")
            {
                pendingBytes = null;
                pendingKind = null;
                var name = pendingName ?? fileKind;
                pendingName = null;
                message = await GroupMedia.Place(api, token, id, fileKind, name, file, submittedReply, operation.Token, topicId: sentTopic);
            }
            else if (submittedEdit is Guid editId)
                message = await api.EditMessageAsync(token, id, editId, new(submittedDraft.Trim()), operation.Token);
            else
                message = sentGroupChannel
                    ? await api.SendTopicMessageAsync(token, id, new(submittedDraft.Trim(), sentTopic, submittedReply), operation.Token)
                    : await api.SendMessageAsync(token, id, new(submittedDraft.Trim(), submittedReply), operation.Token);
            if (!operation.IsCurrent) return;
            if (!CurrentChat(id, ticket))
            {
                if (file is null && channelDrafts.GetValueOrDefault(draftKey) == submittedDraft) channelDrafts.Remove(draftKey);
                return;
            }
            var unchangedDraft = Draft == submittedDraft;
            if (replyTo == submittedReply) replyTo = null;
            if (editing == submittedEdit) editing = null;
            if (replyTo is null && editing is null) HoldCaption = "";
            if (replyTo is null && editing is null && (submittedReply is not null || submittedEdit is not null))
            {
                if (unchangedDraft) Draft = channelDrafts.GetValueOrDefault(draftKey) ?? "";
                else if (Draft.Length > 0) channelDrafts[draftKey] = Draft;
            }
            else if (file is null && unchangedDraft && submittedReply is null && submittedEdit is null) Draft = "";
            var row = Row(message);
            var index = Messages.ToList().FindIndex(item => item.Id == message.MessageId);
            if (index >= 0) Messages[index] = row;
            else Messages.Add(row);
            Status = "";
        }
        catch (CommunityClientException) when (operation.IsCurrent && CurrentChat(id, ticket))
        {
            if (file is not null && CurrentChat(id, ticket) && pendingBytes is null)
            { pendingBytes = file; pendingKind = fileKind; pendingName = fileName; SendCommand.NotifyCanExecuteChanged(); }
            Status = T("groupFailed");
        }
        catch (AccountClientException ex) when (operation.IsCurrent && CurrentChat(id, ticket))
        {
            if (file is not null && CurrentChat(id, ticket) && pendingBytes is null)
            { pendingBytes = file; pendingKind = fileKind; pendingName = fileName; SendCommand.NotifyCanExecuteChanged(); }
            FailSession(ex);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (file is not null && !ReferenceEquals(pendingBytes, file)) CryptographicOperations.ZeroMemory(file);
            if (operation.IsCurrent) Busy(false);
        }
    }

    private bool CanStartRecording(string? kind) => (kind is "voice" or "circle")
        && ShowComposer && !IsBusy && !IsRecording && !IsFinalizingRecording && editing is null && conversationId is not null;

    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private async Task StartRecordingAsync(string? kind)
    {
        if (kind is not ("voice" or "circle") || !CanStartRecording(kind)) return;
        var ticket = navigationGeneration;
        using var operation = App.Work.Enter();
        try
        {
            await recorder.StartAsync(kind, operation.Token);
            if (!operation.IsCurrent || ticket != navigationGeneration)
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
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or IOException
            or PlatformNotSupportedException or System.Runtime.InteropServices.COMException)
        { if (operation.IsCurrent && ticket == navigationGeneration) Status = "Не удалось начать запись. Проверьте доступ к микрофону и камере."; }
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
        if (conversationId is not Guid id || Api is null || Access is null)
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
        var ticket = navigationGeneration;
        ChatCapturedMedia? media = null;
        using var operation = App.Work.Enter();
        Busy(true);
        try
        {
            media = await recorder.FinishAsync(operation.Token);
            ChatMediaLimits.Validate(media);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
            var token = await Access(operation.Token);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
            if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
            var submittedReply = replyTo;
            var message = await GroupMedia.Place(Api, token, id, media.Kind, media.FileName,
                media.Bytes, submittedReply, operation.Token, media.DurationMs, selectedGroupChannel ? selectedTopicId : null);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
            if (replyTo == submittedReply) { replyTo = null; HoldCaption = ""; }
            var row = Row(message);
            var index = Messages.ToList().FindIndex(item => item.Id == row.Id);
            if (index >= 0) Messages[index] = row;
            else Messages.Add(row);
            Status = "";
        }
        catch (OperationCanceledException) { }
        catch (AccountClientException ex) when (operation.IsCurrent && CurrentChat(id, ticket)) { FailSession(ex); }
        catch (Exception ex) when (operation.IsCurrent && CurrentChat(id, ticket) && ex is
            (CommunityClientException or IOException or InvalidDataException or InvalidOperationException
                or UnauthorizedAccessException or System.Runtime.InteropServices.COMException))
        { Status = "Не удалось отправить запись."; }
        finally
        {
            if (media is not null) CryptographicOperations.ZeroMemory(media.Bytes);
            try { await recorder.CancelAsync(); }
            catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException) { }
            finally
            {
                Busy(false);
                IsFinalizingRecording = false;
            }
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

    [RelayCommand]
    private async Task Attach(string? kind)
    {
        if (!ShowComposer || IsBusy || IsRecording || IsFinalizingRecording || kind is not ("image" or "video" or "file") || editing is not null || conversationId is not Guid id || Api is null || Access is null) return;
        var ticket = navigationGeneration;
        var path = await App.FileDialogs.OpenChatMediaAsync(kind);
        if (string.IsNullOrWhiteSpace(path) || IsBusy || !CurrentChat(id, ticket)) return;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 1)
        {
            Status = T("groupFailed");
            return;
        }
        if (info.Length > GroupMedia.MaxBytes)
        {
            Status = "Файл слишком большой.";
            return;
        }
        var bytes = await File.ReadAllBytesAsync(path);
        if (IsBusy || !CurrentChat(id, ticket)) { CryptographicOperations.ZeroMemory(bytes); return; }
        if (pendingBytes is not null) CryptographicOperations.ZeroMemory(pendingBytes);
        pendingKind = kind;
        pendingName = info.Name;
        pendingBytes = bytes;
        SendCommand.NotifyCanExecuteChanged();
        await Send();
    }

    private bool CanSend() => ShowComposer && !IsBusy && !IsRecording && !IsFinalizingRecording && conversationId is not null && (!string.IsNullOrWhiteSpace(Draft) || pendingBytes is { Length: > 0 });

    [RelayCommand]
    private Task BackToGroup()
    {
        if (communityId is not Guid id) return Task.CompletedTask;
        return OpenAsync(id);
    }

    [RelayCommand]
    private void Back()
    {
        navigationGeneration++;
        _ = CancelRecordingAsync();
        StopPlayback();
        conversationId = null;
        groupConversationId = null;
        communityId = null;
        selectedGroupChannel = false;
        selectedTopicId = null;
        activeBallotDraftKey = null;
        CancelCloseBallot();
        ShowBallots = false;
        CanManageChannels = false;
        SelectedChannel = null;
        Channels.Clear();
        Ballots.Clear();
        ClearDesk();
        StartRecordingCommand.NotifyCanExecuteChanged();
        RestartTimer();
        if (pendingBytes is not null) CryptographicOperations.ZeroMemory(pendingBytes);
        pendingBytes = null;
        pendingKind = null;
        pendingName = null;
        Draft = "";
        replyTo = null;
        editing = null;
        HoldCaption = "";
        HasHome = false;
        IsDirect = false;
        Messages.Clear();
        ClearPreviews();
        People.Clear();
        Directs.Clear();
        IsEmpty = Communities.Count == 0;
        Status = "";
        OnPropertyChanged(nameof(HasDirects));
        RaiseList();
    }

    private void RestartTimer()
    {
        timer?.Stop();
        timer = null;
        if (!watching || conversationId is null) return;
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += OnTick;
        timer.Start();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref polling, 1) == 1) return;
        var id = conversationId;
        var ticket = navigationGeneration;
        try
        {
            if (id is Guid current && CurrentChat(current, ticket)) await RefreshChannelsAsync(current, ticket);
            if (id is Guid currentDesk && CurrentChat(currentDesk, ticket)) await RefreshDeskAsync(currentDesk, ticket);
            if (id is Guid selected && CurrentChat(selected, ticket))
            {
                if (ShowBallots) await LoadBallotsAsync(selected, ticket);
                else await PullAsync();
            }
        }
        catch (CommunityClientException) { if (id is Guid current && CurrentChat(current, ticket)) Status = T("groupFailed"); }
        catch (AccountClientException ex) { if (id is Guid current && CurrentChat(current, ticket)) FailSession(ex); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Log.Error("group poll", ex); }
        finally { Interlocked.Exchange(ref polling, 0); }
    }

    private async void ApplyHold(GroupMessageRow row, string action)
    {
        if (conversationId is null || Api is null || Access is null) return;
        if (!Messages.Contains(row)) return;
        var emoji = action.StartsWith("reaction:", StringComparison.Ordinal) ? action["reaction:".Length..] : null;
        var allowed = MessengerHold.Actions(row.Kind, row.Mine, row.Deleted, true);
        if (emoji is null ? !allowed.Contains(action)
            : !allowed.Contains("reaction") || !HoldBox.ReactionChoices.Any(choice => choice.Code == emoji)) return;
        if (action == "reply")
        {
            replyTo = row.Id;
            editing = null;
            Draft = "";
            HoldCaption = "Ответ";
            return;
        }
        if (action == "edit")
        {
            editing = row.Id;
            replyTo = null;
            Draft = row.Body;
            HoldCaption = "Редактирование";
            return;
        }
        if (action == "delete") { AskDeleteMessage(row); return; }
        if (emoji is null) return;
        await ChangeHeldMessageAsync(row, false, emoji);
    }

    private async Task ChangeHeldMessageAsync(GroupMessageRow row, bool delete, string? emoji)
    {
        if (conversationId is not Guid id || Api is null || Access is null || !Messages.Contains(row)) return;
        var ticket = navigationGeneration;
        try
        {
            using var operation = App.Work.Enter();
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent || !CurrentChat(id, ticket)) return;
            var message = delete
                ? await Api.DeleteMessageAsync(token, id, row.Id, operation.Token)
                : await Api.ReactMessageAsync(token, id, row.Id, emoji!, operation.Token);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
            var fresh = Row(message);
            var index = Messages.ToList().FindIndex(item => item.Id == row.Id);
            if (index >= 0) Messages[index] = fresh;
            Status = "";
        }
        catch (CommunityClientException) { if (CurrentChat(id, ticket)) Status = T("groupFailed"); }
        catch (AccountClientException ex) { if (CurrentChat(id, ticket)) FailSession(ex); }
        catch (OperationCanceledException) { }
    }

    private GroupMessageRow Row(ChatMessageResponse message)
    {
        var replyPreview = message.ReplyTo is Guid parentId
            ? Messages.FirstOrDefault(item => item.Id == parentId)?.Display ?? "Сообщение"
            : null;
        GroupMessageRow row = null!;
        row = new(message.MessageId, message.SenderName, message.Body, message.CreatedAt.ToLocalTime().ToString("dd.MM HH:mm"),
            message.SenderId == me, message.Kind, message.Deleted, action => ApplyHold(row, action),
            () => DownloadMediaAsync(row, message.ConversationId), message.Reactions, replyPreview,
            () => PlayMediaAsync(row, message.ConversationId), message.SenderId, message.CreatedAt,
            () => CopyMessageAsync(row));
        if (message.Kind == "image" && !message.Deleted)
        {
            if (previewCache.TryGetValue(message.MessageId, out var cached)) row.Preview = cached;
            else Dispatcher.UIThread.Post(() => _ = LoadPhotoPreviewAsync(message.ConversationId, message.MessageId));
        }
        return row;
    }

    private async Task LoadPhotoPreviewAsync(Guid conversation, Guid messageId)
    {
        if (!previewLoading.Add(messageId) || Api is null || Access is null) return;
        var selectedConversation = conversationId;
        var cancellation = previewCancellation.Token;
        byte[]? bytes = null;
        Bitmap? bitmap = null;
        try
        {
            var token = await Access(cancellation);
            if (string.IsNullOrWhiteSpace(token) || cancellation.IsCancellationRequested || selectedConversation != conversationId) return;
            bytes = await Api.ReadMediaAsync(token, conversation, messageId, cancellation);
            if (cancellation.IsCancellationRequested || selectedConversation != conversationId) return;
            bitmap = ChatMediaPreview.DecodePhoto(bytes);
            previewCache[messageId] = bitmap;
            foreach (var row in Messages.Where(row => row.Id == messageId)) row.Preview = bitmap;
            bitmap = null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or CommunityClientException or AccountClientException
            or ArgumentException or InvalidDataException or IOException) { }
        finally
        {
            bitmap?.Dispose();
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            previewLoading.Remove(messageId);
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

    private async Task PlayMediaAsync(GroupMessageRow row, Guid id)
    {
        if (!row.CanPlay || !Messages.Contains(row) || Api is null || Access is null) return;
        if (playingMessage == row.Id) { StopPlayback(); return; }
        StopPlayback();
        var ticket = navigationGeneration;
        byte[]? bytes = null;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrWhiteSpace(token) || !operation.IsCurrent || !CurrentChat(id, ticket)) return;
            bytes = await Api.ReadMediaAsync(token, id, row.Id, operation.Token);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
            await player.PlayAsync(row.Kind, null, bytes, operation.Token);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) { player.Stop(); return; }
            if (row.Kind == "voice") { playingMessage = row.Id; row.IsPlaying = true; }
            Status = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is CommunityClientException or AccountClientException or IOException or InvalidDataException
            or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { if (operation.IsCurrent && CurrentChat(id, ticket)) Status = "Не удалось воспроизвести запись."; }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }

    private void StopPlayback()
    {
        player.Stop();
        playingMessage = null;
        foreach (var row in Messages) row.IsPlaying = false;
    }

    private async Task DownloadMediaAsync(GroupMessageRow row, Guid id)
    {
        if (!row.CanDownload || !Messages.Contains(row) || Api is null || Access is null) return;
        var ticket = navigationGeneration;
        if (!CurrentChat(id, ticket)) return;
        byte[]? bytes = null;
        using var operation = App.Work.Enter();
        try
        {
            var path = await App.FileDialogs.SaveChatMediaAsync(GroupMedia.SafeName(row.Body, row.Kind));
            if (string.IsNullOrWhiteSpace(path) || !operation.IsCurrent || !CurrentChat(id, ticket)) return;
            if (File.Exists(path)) { Status = "Файл уже существует. Выберите новое имя."; return; }
            var token = await Access(operation.Token);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
            if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
            bytes = await Api.ReadMediaAsync(token, id, row.Id, operation.Token);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, operation.Token);
                if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
                File.Move(temporary, path);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            Status = T("groupSavedMedia");
        }
        catch (OperationCanceledException) { }
        catch (AccountClientException ex) when (operation.IsCurrent && CurrentChat(id, ticket)) { FailSession(ex); }
        catch (Exception ex) when (operation.IsCurrent && CurrentChat(id, ticket) && ex is (CommunityClientException or IOException or UnauthorizedAccessException))
        { Status = T("groupMediaFailed"); }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }

    private void FailSession(AccountClientException ex)
    {
        if (ex.Failure is AccountClientFailure.InvalidSession or AccountClientFailure.ReauthenticationRequired
            or AccountClientFailure.SessionChanged) ShowAccount();
        else Status = T("groupFailed");
    }

    private void ShowAccount()
    {
        navigationGeneration++;
        _ = CancelRecordingAsync();
        StopPlayback();
        conversationId = null;
        groupConversationId = null;
        communityId = null;
        StartRecordingCommand.NotifyCanExecuteChanged();
        if (pendingBytes is not null) CryptographicOperations.ZeroMemory(pendingBytes);
        pendingBytes = null;
        pendingKind = null;
        pendingName = null;
        channelDrafts.Clear();
        ballotDrafts.Clear();
        activeBallotDraftKey = null;
        CancelCloseBallot();
        selectedGroupChannel = false;
        selectedTopicId = null;
        ShowBallots = false;
        CanManageChannels = false;
        SelectedChannel = null;
        Channels.Clear();
        Ballots.Clear();
        ClearDesk();
        Draft = "";
        replyTo = null;
        editing = null;
        HoldCaption = "";
        RestartTimer();
        NeedAccount = true;
        HasHome = false;
        IsEmpty = false;
        Messages.Clear();
        ClearPreviews();
        Status = "";
        RaiseList();
    }

    private void RaiseList() => OnPropertyChanged(nameof(ShowList));
    private bool CurrentChat(Guid id, int ticket) => conversationId == id && navigationGeneration == ticket;
    private string Role(string? role) => role switch
    {
        "headman" => T("groupRoleHeadman"),
        "curator" => T("groupRoleCurator"),
        _ => T("groupRoleMember")
    };
}

public sealed class GroupCommunityRow(string name, string role, IRelayCommand open)
{
    public string Name { get; } = name;
    public string Role { get; } = role;
    public IRelayCommand OpenCommand { get; } = open;
}

public sealed class GroupPersonRow(string name, string detail, string role, string preview, string unread, bool self, IRelayCommand? open)
{
    public string Name { get; } = name;
    public string Detail { get; } = detail;
    public string Role { get; } = role;
    public string Preview { get; } = preview;
    public int UnreadCount { get; } = int.TryParse(unread, out var count) ? count : 0;
    public string Unread => UnreadBadge.Label(UnreadCount);
    public string UnreadDescription => UnreadBadge.Description(UnreadCount);
    public bool Self { get; } = self;
    public IRelayCommand? OpenCommand { get; } = open;
}

public sealed partial class GroupMessageRow(Guid id, string author, string body, string when, bool mine, string kind = "text", bool deleted = false, Action<string>? apply = null, Func<Task>? download = null, IReadOnlyList<ChatReactionSummary>? reactions = null, string? replyPreview = null, Func<Task>? play = null, Guid senderId = default, DateTimeOffset createdAt = default, Func<Task>? copy = null) : ObservableObject
{
    public Guid Id { get; } = id;
    public string Author { get; } = author;
    public Guid SenderId { get; } = senderId;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public string Body { get; } = body;
    public string Display { get; } = deleted ? "Сообщение удалено" : kind switch
    {
        "image" => "Фото",
        "video" => "Видео",
        "circle" => "Кружок",
        "voice" => "Голосовое",
        "file" => string.IsNullOrWhiteSpace(body) ? "Документ" : body,
        _ => body
    };
    public string When { get; } = when;
    public bool Mine { get; } = mine;
    public string Kind { get; } = kind;
    public bool Deleted { get; } = deleted;
    [ObservableProperty] private string dayHeader = "";
    [ObservableProperty] private bool showAuthor;
    public bool IsReply => replyPreview is not null;
    public string ReplyPreview { get; } = replyPreview is null ? "" : "↳ " + replyPreview[..Math.Min(replyPreview.Length, 80)];
    public IReadOnlyList<ChatReactionSummary> ReactionSummaries { get; } = reactions ?? [];
    public IReadOnlyList<GroupReactionRow> Reactions { get; } = (reactions ?? [])
        .Where(reaction => reaction.Count > 0)
        .Select(reaction => new GroupReactionRow(reaction)).ToArray();
    public bool HasReactions => Reactions.Count > 0;
    public bool CanDownload => download is not null && !Deleted && Kind is ("image" or "video" or "file" or "voice" or "circle");
    public IAsyncRelayCommand? DownloadCommand { get; } = download is null ? null : new AsyncRelayCommand(download);
    public bool CanPlay => play is not null && !Deleted && Kind is ("voice" or "circle");
    public IAsyncRelayCommand? PlayCommand { get; } = play is null ? null : new AsyncRelayCommand(play);
    public bool CanCopy => copy is not null && !Deleted && Kind == "text" && !string.IsNullOrWhiteSpace(Body);
    public IAsyncRelayCommand? CopyCommand { get; } = copy is null ? null : new AsyncRelayCommand(copy);
    public string PlayCaption => Kind == "circle" ? "Смотреть кружок" : IsPlaying ? "Остановить" : "Слушать";
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private Bitmap? preview;
    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayCaption));
    public void Apply(string action) => apply?.Invoke(action);
}

public sealed class GroupReactionRow(ChatReactionSummary reaction)
{
    public string Display => reaction.Emoji switch
    {
        "like" => "👍", "heart" => "❤️", "laugh" => "😂", "wow" => "😮", "sad" => "😢",
        _ => reaction.Emoji
    } + $" {reaction.Count}" + (reaction.Mine ? " ✓" : "");
    public bool Mine => reaction.Mine;
}
