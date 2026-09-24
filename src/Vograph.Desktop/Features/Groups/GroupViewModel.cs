using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Services;
using Vograph.Desktop.ViewModels;
using Zapara.Client.Domain;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel : ViewModelBase
{
    private readonly CommunityHttpClient? client;
    private readonly Func<CancellationToken, Task<string?>>? accessToken;
    // Built before UseCommunities: keep reading the live session instead of the null captured here.
    private CommunityHttpClient? Api => client ?? App.Communities;
    private Func<CancellationToken, Task<string?>>? Access => accessToken ?? App.CommunityAccess;
    private int polling;
    private Guid me;
    private Guid? conversationId;
    private Guid? communityId;
    private readonly Dictionary<Guid, string> drafts = new();
    private int navigationGeneration;
    private int busyDepth;
    private bool watching;
    private DispatcherTimer? timer;

    public GroupViewModel(AppServices app) : base(app)
    {
        client = app.Communities;
        accessToken = app.CommunityAccess;
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
    private Guid? replyTo;
    private Guid? editing;
    private string? pendingKind;
    private string? pendingName;
    private byte[]? pendingBytes;
    public bool ShowList => !NeedAccount && !HasHome && !IsEmpty;
    public bool HasDirects => Directs.Count > 0;

    public override void Detach() => Watch(false);
    public override Task ActivateAsync() => LoadAsync();
    public void Watch(bool visible) { watching = visible; RestartTimer(); }

    partial void OnNeedAccountChanged(bool value) => RaiseList();
    partial void OnIsEmptyChanged(bool value) => RaiseList();
    partial void OnHasHomeChanged(bool value) => RaiseList();
    partial void OnDraftChanged(string value)
    {
        if (conversationId is Guid id && editing is null && replyTo is null)
        {
            if (value.Length == 0) drafts.Remove(id);
            else drafts[id] = value;
        }
        SendCommand.NotifyCanExecuteChanged();
    }
    private void Busy(bool value)
    {
        busyDepth = Math.Max(0, busyDepth + (value ? 1 : -1));
        IsBusy = busyDepth != 0;
        SendCommand.NotifyCanExecuteChanged();
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
            var preferred = rows.FirstOrDefault(item => item.Name == wanted || item.Name == "Группа " + wanted) ?? (rows.Length == 1 ? rows[0] : null);
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
            await OpenConversationAsync(home.GroupChat.ConversationId, T("groupChat"), false);
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
        People.Clear();
        foreach (var person in home.Classmates)
            People.Add(new(person.DisplayName ?? person.Username, "@" + person.Username, Role(person.Role), "", "", person.Self,
                person.Self ? null : new RelayCommand(() => _ = OpenDirectAsync(person.UserId, person.DisplayName ?? person.Username))));
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

    private async Task OpenConversationAsync(Guid id, string title, bool direct)
    {
        var ticket = ++navigationGeneration;
        conversationId = id;
        pendingBytes = null;
        pendingKind = null;
        pendingName = null;
        ChatTitle = title;
        IsDirect = direct;
        Draft = drafts.GetValueOrDefault(id) ?? "";
        replyTo = null;
        editing = null;
        HoldCaption = "";
        Messages.Clear();
        HasMore = false;
        try
        {
            await LoadLatestAsync(id, ticket);
            if (!CurrentChat(id, ticket)) return;
            if (Api is not null && Access is not null)
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
        var page = await Api.MessagesAsync(token, id, ct: operation.Token);
        if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
        Messages.Clear();
        foreach (var message in page.Messages) Messages.Add(Row(message));
        HasMore = page.HasMore;
    }

    [RelayCommand]
    private async Task LoadOlder()
    {
        if (conversationId is not Guid id || Messages.Count == 0 || Api is null || Access is null) return;
        var ticket = navigationGeneration;
        using var operation = App.Work.Enter();
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
            if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
            var page = await Api.MessagesAsync(token, id, before: Messages[0].Id, ct: operation.Token);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
            HasMore = page.HasMore;
            for (var i = page.Messages.Count - 1; i >= 0; i--)
                if (Messages.All(item => item.Id != page.Messages[i].MessageId)) Messages.Insert(0, Row(page.Messages[i]));
        }
        catch (CommunityClientException) when (operation.IsCurrent && ticket == navigationGeneration) { Status = T("groupFailed"); }
        catch (AccountClientException ex) when (operation.IsCurrent && ticket == navigationGeneration) { FailSession(ex); }
        catch (OperationCanceledException) { }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    private async Task PullAsync()
    {
        if (conversationId is not Guid id || Api is null || Access is null) return;
        var ticket = navigationGeneration;
        using var operation = App.Work.Enter();
        var token = await Access(operation.Token);
        if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
        if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
        var page = await Api.MessagesAsync(token, id, ct: operation.Token);
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
                var next = await Api.MessagesAsync(token, id, after: cursor, ct: operation.Token);
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
            else if (Messages[index].Body != message.Body || Messages[index].Deleted != message.Deleted || Messages[index].Kind != message.Kind)
                Messages[index] = Row(message);
        }
        if (receivedFromOther && reachedLatest && operation.IsCurrent && CurrentChat(id, ticket))
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
        if (conversationId is not Guid id || api is null || Access is null || (string.IsNullOrWhiteSpace(Draft) && file is null)) return;
        var ticket = navigationGeneration;
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
                message = await GroupMedia.Place(api, token, id, fileKind, name, file, submittedReply, operation.Token);
            }
            else if (submittedEdit is Guid editId)
                message = await api.EditMessageAsync(token, id, editId, new(submittedDraft.Trim()), operation.Token);
            else
                message = await api.SendMessageAsync(token, id, new(submittedDraft.Trim(), submittedReply), operation.Token);
            if (!operation.IsCurrent) return;
            if (!CurrentChat(id, ticket))
            {
                if (file is null && drafts.GetValueOrDefault(id) == submittedDraft) drafts.Remove(id);
                return;
            }
            var unchangedDraft = Draft == submittedDraft;
            if (replyTo == submittedReply) replyTo = null;
            if (editing == submittedEdit) editing = null;
            if (replyTo is null && editing is null) HoldCaption = "";
            if (replyTo is null && editing is null && (submittedReply is not null || submittedEdit is not null))
            {
                if (unchangedDraft) Draft = drafts.GetValueOrDefault(id) ?? "";
                else if (Draft.Length > 0) drafts[id] = Draft;
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
        finally { if (operation.IsCurrent) Busy(false); }
    }

    [RelayCommand]
    private async Task Attach(string? kind)
    {
        if (IsBusy || kind is not ("image" or "video" or "file") || editing is not null || conversationId is not Guid id || Api is null || Access is null) return;
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
        if (IsBusy || !CurrentChat(id, ticket)) return;
        pendingKind = kind;
        pendingName = info.Name;
        pendingBytes = bytes;
        SendCommand.NotifyCanExecuteChanged();
        await Send();
    }

    private bool CanSend() => !IsBusy && conversationId is not null && (!string.IsNullOrWhiteSpace(Draft) || pendingBytes is { Length: > 0 });

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
        conversationId = null;
        RestartTimer();
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
        try { await PullAsync(); }
        catch (CommunityClientException) { if (id is Guid current && CurrentChat(current, ticket)) Status = T("groupFailed"); }
        catch (AccountClientException ex) { if (id is Guid current && CurrentChat(current, ticket)) FailSession(ex); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Log.Error("group poll", ex); }
        finally { Interlocked.Exchange(ref polling, 0); }
    }

    private async void ApplyHold(GroupMessageRow row, string action)
    {
        if (conversationId is not Guid id || Api is null || Access is null) return;
        if (!Messages.Contains(row)) return;
        var ticket = navigationGeneration;
        if (!MessengerHold.Actions(row.Kind, row.Mine, row.Deleted, true).Contains(action)) return;
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
        try
        {
            using var operation = App.Work.Enter();
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent || !CurrentChat(id, ticket)) return;
            var message = action == "delete"
                ? await Api.DeleteMessageAsync(token, id, row.Id, operation.Token)
                : await Api.ReactMessageAsync(token, id, row.Id, "like", operation.Token);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
            var fresh = Row(message);
            var index = Messages.ToList().FindIndex(item => item.Id == row.Id);
            if (index >= 0) Messages[index] = fresh;
            Status = action == "reaction" ? "Реакция" : "";
        }
        catch (CommunityClientException) { if (CurrentChat(id, ticket)) Status = T("groupFailed"); }
        catch (AccountClientException ex) { if (CurrentChat(id, ticket)) FailSession(ex); }
        catch (OperationCanceledException) { }
    }

    private GroupMessageRow Row(ChatMessageResponse message)
    {
        GroupMessageRow row = null!;
        row = new(message.MessageId, message.SenderName, message.Body, message.CreatedAt.ToLocalTime().ToString("dd.MM HH:mm"),
            message.SenderId == me, message.Kind, message.Deleted, action => ApplyHold(row, action),
            () => DownloadMediaAsync(row, message.ConversationId));
        return row;
    }

    private async Task DownloadMediaAsync(GroupMessageRow row, Guid id)
    {
        if (!row.CanDownload || !Messages.Contains(row) || Api is null || Access is null) return;
        var ticket = navigationGeneration;
        if (!CurrentChat(id, ticket)) return;
        using var operation = App.Work.Enter();
        try
        {
            var path = await App.FileDialogs.SaveChatMediaAsync(GroupMedia.SafeName(row.Body, row.Kind));
            if (string.IsNullOrWhiteSpace(path) || !operation.IsCurrent || !CurrentChat(id, ticket)) return;
            var token = await Access(operation.Token);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
            if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
            var bytes = await Api.ReadMediaAsync(token, id, row.Id, operation.Token);
            if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, operation.Token);
                if (!operation.IsCurrent || !CurrentChat(id, ticket)) return;
                File.Move(temporary, path, overwrite: true);
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
        conversationId = null;
        pendingBytes = null;
        pendingKind = null;
        pendingName = null;
        drafts.Clear();
        Draft = "";
        replyTo = null;
        editing = null;
        HoldCaption = "";
        RestartTimer();
        NeedAccount = true;
        HasHome = false;
        IsEmpty = false;
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
    public string Unread { get; } = unread;
    public bool Self { get; } = self;
    public IRelayCommand? OpenCommand { get; } = open;
}

public sealed class GroupMessageRow(Guid id, string author, string body, string when, bool mine, string kind = "text", bool deleted = false, Action<string>? apply = null, Func<Task>? download = null)
{
    public Guid Id { get; } = id;
    public string Author { get; } = author;
    public string Body { get; } = body;
    public string Display { get; } = deleted ? "Сообщение удалено" : kind switch
    {
        "image" => "Фото",
        "video" or "circle" => "Видео",
        "voice" => "Голосовое",
        "file" => string.IsNullOrWhiteSpace(body) ? "Документ" : body,
        _ => body
    };
    public string When { get; } = when;
    public bool Mine { get; } = mine;
    public string Kind { get; } = kind;
    public bool Deleted { get; } = deleted;
    public bool CanDownload => download is not null && !Deleted && Kind is ("image" or "video" or "file");
    public IAsyncRelayCommand? DownloadCommand { get; } = download is null ? null : new AsyncRelayCommand(download);
    public void Apply(string action) => apply?.Invoke(action);
}
