using System.Collections.ObjectModel;
using System.Security.Cryptography;
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
    private int generation;
    private Guid? conversationId;
    private Guid? replyTo;
    private Guid? editing;
    private Guid? peerId;
    private DispatcherTimer? timer;
    private bool watching;
    private int polling;
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

    public override void Detach() => Watch(false);

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
        CommunityHttpClient? communityClient = null, Func<CancellationToken, Task<string?>>? access = null) : base(app)
    {
        this.shell = shell;
        this.client = client;
        this.communityClient = communityClient;
        this.access = access;
        NeedAccount = (Social is null && Communities is null) || Access is null;
    }

    public ObservableCollection<ChatInboxRow> Chats { get; } = [];
    public ObservableCollection<ChatInviteRow> Incoming { get; } = [];
    public ObservableCollection<ChatMessageRow> Messages { get; } = [];
    [ObservableProperty] private bool needAccount;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string myCode = "";
    [ObservableProperty] private string inviteCode = "";
    [ObservableProperty] private string chatTitle = "";
    [ObservableProperty] private string draft = "";
    [ObservableProperty] private string actionCaption = "";
    [ObservableProperty] private bool hasMore;
    public bool HasConversation => conversationId is not null;

    public override Task ActivateAsync() => RefreshCoreAsync(background: false);

    [RelayCommand]
    private Task RefreshAsync() => RefreshCoreAsync(background: false);

    private async Task RefreshCoreAsync(bool background)
    {
        var ticket = background ? generation : ++generation;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent || (Social is null && Communities is null) || Access is null) { NeedAccount = true; return; }
        if (!background) IsBusy = true;
        try
        {
            var token = await Access(operation.Token);
            if (!operation.IsCurrent || ticket != generation) return;
            if (string.IsNullOrWhiteSpace(token)) { NeedAccount = true; return; }
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
                                new RelayCommand(() => OpenGroup(group.CommunityId))));
                            foreach (var direct in detail.Directs)
                                rows.Add(new(direct.ConversationId, group.CommunityId, false,
                                    $"{detail.GroupName ?? group.Name} · {direct.Title}",
                                    direct.LastBody ?? "Пока нет сообщений", direct.LastAt, direct.Unread,
                                    new RelayCommand(() => OpenGroupDirect(group.CommunityId, direct.ConversationId)),
                                    "Личный в группе"));
                        }
                        catch (CommunityClientException) { groupsUnavailable = true; }
                    }
                }
                catch (CommunityClientException) { groupsUnavailable = true; }
            foreach (var friend in home?.Friends ?? [])
                rows.Add(new(friend.ConversationId, null, true, friend.DisplayName ?? friend.Username,
                    friend.LastBody ?? "Пока нет сообщений", friend.LastAt, friend.Unread,
                    new RelayCommand(() => _ = OpenPersonalAsync(friend))));
            if (!operation.IsCurrent || ticket != generation) return;
            if (groupsUnavailable)
                foreach (var previous in Chats.Where(row => !row.Personal && rows.All(next => next.ConversationId != row.ConversationId)))
                    rows.Add(previous);
            if (socialUnavailable)
                foreach (var previous in Chats.Where(row => row.Personal)) rows.Add(previous);
            NeedAccount = false;
            if (home is not null) MyCode = home.Code;
            Chats.Clear();
            foreach (var row in rows.OrderByDescending(r => r.LastAt)) Chats.Add(row);
            if (home is not null)
            {
                Incoming.Clear();
                foreach (var invite in home.Incoming)
                    Incoming.Add(new(invite.DisplayName ?? invite.Username,
                        new AsyncRelayCommand(() => AcceptAsync(invite.FriendshipId)),
                        new AsyncRelayCommand(() => DeclineAsync(invite.FriendshipId))));
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
        { if (operation.IsCurrent && ticket == generation) Status = "Не удалось загрузить чаты."; }
        finally { if (!background && operation.IsCurrent) IsBusy = false; }
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
        conversationId = friend.ConversationId;
        peerId = friend.UserId;
        ChatTitle = friend.DisplayName ?? friend.Username;
        Draft = "";
        replyTo = null;
        editing = null;
        ActionCaption = "";
        Messages.Clear();
        HasMore = false;
        OnPropertyChanged(nameof(HasConversation));
        await LoadMessagesAsync(ticket);
    }

    private async Task LoadMessagesAsync(int ticket, Guid? before = null)
    {
        if (conversationId is not Guid id || Social is null || Access is null) return;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent || ticket != generation) return;
            var page = await Social.MessagesAsync(token, id, before, operation.Token);
            if (!operation.IsCurrent || ticket != generation) return;
            if (before is null) Messages.Clear();
            var olderIndex = 0;
            foreach (var message in page.Messages)
            {
                if (Messages.Any(row => row.Id == message.MessageId)) continue;
                if (before is null) Messages.Add(Row(message));
                else Messages.Insert(olderIndex++, Row(message));
            }
            HasMore = page.HasMore;
            Status = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException)
        { if (operation.IsCurrent && ticket == generation) Status = "Не удалось загрузить сообщения."; }
    }

    private async Task PullLatestAsync()
    {
        if (!watching || conversationId is not Guid id || Social is null || Access is null) return;
        var ticket = generation;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent || !watching || ticket != generation || conversationId != id) return;
            var page = await Social.MessagesAsync(token, id, ct: operation.Token);
            if (!operation.IsCurrent || !watching || ticket != generation || conversationId != id) return;
            var fresh = page.Messages.ToList();
            var known = Messages.Select(row => row.Id).ToHashSet();
            var cursors = new HashSet<Guid>();
            while (known.Count > 0 && page.HasMore && fresh.All(message => !known.Contains(message.MessageId)))
            {
                if (page.Messages.Count == 0 || !cursors.Add(page.Messages[0].MessageId))
                    throw new SocialClientException(0);
                page = await Social.MessagesAsync(token, id, page.Messages[0].MessageId, operation.Token);
                if (!operation.IsCurrent || !watching || ticket != generation || conversationId != id) return;
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
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException)
        { if (operation.IsCurrent && watching && ticket == generation) Status = "Не удалось обновить сообщения."; }
    }

    [RelayCommand]
    private Task LoadOlderAsync() => HasMore && Messages.Count > 0
        ? LoadMessagesAsync(generation, Messages[0].Id) : Task.CompletedTask;

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

    private async Task AcceptAsync(Guid friendshipId)
    {
        if (Social is null || Access is null) return;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent) return;
            await Social.AcceptAsync(token, friendshipId, operation.Token);
            if (operation.IsCurrent) await RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException)
        { if (operation.IsCurrent) Status = "Не удалось принять приглашение."; }
    }

    private async Task DeclineAsync(Guid friendshipId)
    {
        if (Social is null || Access is null) return;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent) return;
            await Social.DeclineAsync(token, friendshipId, operation.Token);
            if (operation.IsCurrent) await RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException)
        { if (operation.IsCurrent) Status = "Не удалось отклонить приглашение."; }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (conversationId is not Guid id || Social is null || Access is null || string.IsNullOrWhiteSpace(Draft)) return;
        var ticket = generation;
        var body = Draft.Trim();
        var selectedEdit = editing;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent || ticket != generation) return;
            var response = selectedEdit is Guid messageId
                ? await Social.EditAsync(token, id, messageId, body, operation.Token)
                : await Social.SendTextAsync(token, id, body, replyTo, operation.Token);
            if (!operation.IsCurrent || ticket != generation) return;
            var previous = Messages.ToList().FindIndex(row => row.Id == response.MessageId);
            if (previous >= 0) Messages[previous] = Row(response);
            else Messages.Add(Row(response));
            if (Draft == body) Draft = "";
            editing = null;
            replyTo = null;
            ActionCaption = "";
            Status = "";
            await RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException)
        { if (operation.IsCurrent && ticket == generation) Status = "Не удалось отправить сообщение."; }
    }

    [RelayCommand]
    private async Task AttachAsync(string? kind)
    {
        if (kind is not ("image" or "file") || conversationId is not Guid id || Social is null || Access is null) return;
        var ticket = generation;
        var path = await App.FileDialogs.OpenChatMediaAsync(kind);
        if (string.IsNullOrWhiteSpace(path) || ticket != generation || conversationId != id) return;
        byte[]? bytes = null;
        using var operation = App.Work.Enter();
        try
        {
            var file = new FileInfo(path);
            var limit = kind == "image" ? 25L * 1024 * 1024 : 20L * 1024 * 1024;
            if (!file.Exists || file.Length is < 1 || file.Length > limit)
            {
                Status = "Файл слишком большой или недоступен.";
                return;
            }
            bytes = await File.ReadAllBytesAsync(path, operation.Token);
            if (!operation.IsCurrent || ticket != generation || conversationId != id) return;
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent || ticket != generation) return;
            var submittedReply = replyTo;
            var message = await Social.SendMediaAsync(token, id, kind, file.Name, bytes, submittedReply, operation.Token);
            if (!operation.IsCurrent || ticket != generation || conversationId != id) return;
            Messages.Add(Row(message));
            if (replyTo == submittedReply) { replyTo = null; ActionCaption = ""; }
            Status = "";
            await RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException or IOException or UnauthorizedAccessException)
        { if (operation.IsCurrent && ticket == generation) Status = "Не удалось отправить файл."; }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }

    private ChatMessageRow Row(SocialMessageResponse message) => new(message,
        message.SenderId != peerId,
        new RelayCommand(() => { replyTo = message.MessageId; editing = null; Draft = ""; ActionCaption = "Ответ"; }),
        new RelayCommand(() => { editing = message.MessageId; replyTo = null; Draft = message.Body ?? ""; ActionCaption = "Редактирование"; }),
        new AsyncRelayCommand(() => ChangeMessageAsync(message.MessageId, true)),
        new AsyncRelayCommand(() => ChangeMessageAsync(message.MessageId, false)),
        message.AttachmentId is Guid && !message.Deleted && message.Kind is "image" or "file"
            ? new AsyncRelayCommand(() => SaveAttachmentAsync(message)) : null);

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
        if (clean is "" or "." or "..") clean = kind == "image" ? "Фото.webp" : "Документ";
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
            if (!operation.IsCurrent || ticket != generation) return;
            var index = Messages.ToList().FindIndex(row => row.Id == messageId);
            if (index >= 0) Messages[index] = Row(changed);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is SocialClientException or AccountClientException)
        { if (operation.IsCurrent && ticket == generation) Status = "Не удалось изменить сообщение."; }
    }
}

public sealed class ChatInboxRow(Guid conversationId, Guid? communityId, bool personal, string title,
    string preview, DateTimeOffset? lastAt, int unread, IRelayCommand open, string? kind = null)
{
    public Guid ConversationId { get; } = conversationId;
    public Guid? CommunityId { get; } = communityId;
    public bool Personal { get; } = personal;
    public string Kind => kind ?? (Personal ? "Личный" : "Группа");
    public string Title { get; } = title;
    public string Preview { get; } = preview;
    public DateTimeOffset? LastAt { get; } = lastAt;
    public string Unread => unread > 0 ? unread.ToString() : "";
    public IRelayCommand OpenCommand { get; } = open;
}

public sealed class ChatInviteRow(string name, IAsyncRelayCommand accept, IAsyncRelayCommand decline)
{
    public string Name { get; } = name;
    public IAsyncRelayCommand AcceptCommand { get; } = accept;
    public IAsyncRelayCommand DeclineCommand { get; } = decline;
}

public sealed class ChatMessageRow(SocialMessageResponse response, bool mine, IRelayCommand reply,
    IRelayCommand edit, IAsyncRelayCommand delete, IAsyncRelayCommand react, IAsyncRelayCommand? download)
{
    public Guid Id => response.MessageId;
    public string Author => response.SenderName;
    public string Display => response.Deleted ? "Сообщение удалено" : response.Kind switch
    {
        "image" => "Фото", "file" => response.FileName ?? "Документ", _ => response.Body ?? response.Kind
    };
    public string Detail => response.ReplyBody is null ? "" : "Ответ: " + response.ReplyBody;
    public string When => response.CreatedAt.ToLocalTime().ToString("dd.MM HH:mm");
    public bool Mine { get; } = mine;
    public bool CanEdit => Mine && !response.Deleted && response.Kind == "text";
    public bool CanDelete => Mine && !response.Deleted;
    public bool CanReact => !response.Deleted;
    public string Reactions => string.Join(" ", response.Reactions.Select(r => $"{r.Emoji} {r.Count}"));
    public IRelayCommand ReplyCommand { get; } = reply;
    public IRelayCommand EditCommand { get; } = edit;
    public IAsyncRelayCommand DeleteCommand { get; } = delete;
    public IAsyncRelayCommand ReactCommand { get; } = react;
    public bool CanDownload => DownloadCommand is not null;
    public IAsyncRelayCommand? DownloadCommand { get; } = download;
}
