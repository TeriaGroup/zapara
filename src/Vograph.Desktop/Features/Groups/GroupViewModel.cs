using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Services;
using Vograph.Desktop.ViewModels;
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
    public bool ShowList => !NeedAccount && !HasHome && !IsEmpty;
    public bool HasDirects => Directs.Count > 0;

    public override void Detach() => Watch(false);
    public override Task ActivateAsync() => LoadAsync();
    public void Watch(bool visible) { watching = visible; RestartTimer(); }

    partial void OnNeedAccountChanged(bool value) => RaiseList();
    partial void OnIsEmptyChanged(bool value) => RaiseList();
    partial void OnHasHomeChanged(bool value) => RaiseList();
    partial void OnDraftChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    private void Busy(bool value)
    {
        IsBusy = value;
        SendCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadAsync()
    {
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
            if (!operation.IsCurrent) return;
            if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
            NeedAccount = false;
            var wanted = await RunAsync(() =>
            {
                var id = App.Db.GetSettings().MyGroupId;
                return string.IsNullOrEmpty(id) ? "" : App.Db.GetGroup(id)?.Name ?? "";
            }, "моя группа") ?? "";
            var rows = (await Api.ListAsync(token, ct: operation.Token)).Where(item => item.Role is not null).ToArray();
            if (!operation.IsCurrent) return;
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
        catch (CommunityClientException) when (operation.IsCurrent) { Status = T("groupFailed"); }
        catch (AccountClientException ex) when (operation.IsCurrent) { FailSession(ex); }
        catch (OperationCanceledException) { }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    private async Task OpenAsync(Guid id)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent || Api is null || Access is null) return;
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token) || !operation.IsCurrent) return;
            var home = await Api.GroupHomeAsync(token, id, operation.Token);
            if (!operation.IsCurrent) return;
            Show(home);
            await OpenConversationAsync(home.GroupChat.ConversationId, T("groupChat"), false);
        }
        catch (CommunityClientException) when (operation.IsCurrent) { Status = T("groupFailed"); }
        catch (AccountClientException ex) when (operation.IsCurrent) { FailSession(ex); }
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
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
            var conversation = await Api.OpenDirectAsync(token, new(community, userId), operation.Token);
            var home = await Api.GroupHomeAsync(token, community, operation.Token);
            if (!operation.IsCurrent) return;
            Show(home);
            await OpenConversationAsync(conversation.ConversationId, title, true);
        }
        catch (CommunityClientException) when (operation.IsCurrent) { Status = T("groupFailed"); }
        catch (AccountClientException ex) when (operation.IsCurrent) { FailSession(ex); }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    private async Task OpenConversationAsync(Guid id, string title, bool direct)
    {
        conversationId = id;
        ChatTitle = title;
        IsDirect = direct;
        Messages.Clear();
        HasMore = false;
        await LoadLatestAsync();
        if (Api is not null && Access is not null)
        {
            try
            {
                using var operation = App.Work.Enter();
                var token = await Access(operation.Token);
                if (string.IsNullOrEmpty(token)) { if (operation.IsCurrent) ShowAccount(); }
                else if (operation.IsCurrent) await Api.MarkReadAsync(token, id, operation.Token);
            }
            catch (CommunityClientException) { }
            catch (AccountClientException ex) { FailSession(ex); }
        }
        RestartTimer();
    }

    private async Task LoadLatestAsync()
    {
        if (conversationId is not Guid id || Api is null || Access is null) return;
        using var operation = App.Work.Enter();
        var token = await Access(operation.Token);
        if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
        var page = await Api.MessagesAsync(token, id, ct: operation.Token);
        if (!operation.IsCurrent) return;
        Messages.Clear();
        foreach (var message in page.Messages) Messages.Add(Row(message));
        HasMore = page.HasMore;
    }

    [RelayCommand]
    private async Task LoadOlder()
    {
        if (conversationId is not Guid id || Messages.Count == 0 || Api is null || Access is null) return;
        using var operation = App.Work.Enter();
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
            var page = await Api.MessagesAsync(token, id, before: Messages[0].Id, ct: operation.Token);
            if (!operation.IsCurrent) return;
            HasMore = page.HasMore;
            for (var i = page.Messages.Count - 1; i >= 0; i--)
                if (Messages.All(item => item.Id != page.Messages[i].MessageId)) Messages.Insert(0, Row(page.Messages[i]));
        }
        catch (CommunityClientException) when (operation.IsCurrent) { Status = T("groupFailed"); }
        catch (AccountClientException ex) when (operation.IsCurrent) { FailSession(ex); }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    private async Task PullAsync()
    {
        if (conversationId is not Guid id || Api is null || Access is null) return;
        using var operation = App.Work.Enter();
        var token = await Access(operation.Token);
        if (!operation.IsCurrent) return;
        if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
        var page = Messages.Count == 0
            ? await Api.MessagesAsync(token, id, ct: operation.Token)
            : await Api.MessagesAsync(token, id, after: Messages[^1].Id, ct: operation.Token);
        if (!operation.IsCurrent) return;
        foreach (var message in page.Messages)
            if (Messages.All(item => item.Id != message.MessageId)) Messages.Add(Row(message));
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        if (conversationId is not Guid id || Api is null || Access is null || string.IsNullOrWhiteSpace(Draft)) return;
        using var operation = App.Work.Enter();
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrEmpty(token)) { ShowAccount(); return; }
            var message = await Api.SendMessageAsync(token, id, new(Draft.Trim()), operation.Token);
            if (!operation.IsCurrent) return;
            Draft = "";
            if (Messages.All(item => item.Id != message.MessageId)) Messages.Add(Row(message));
            Status = "";
        }
        catch (CommunityClientException) when (operation.IsCurrent) { Status = T("groupFailed"); }
        catch (AccountClientException ex) when (operation.IsCurrent) { FailSession(ex); }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(Draft) && conversationId is not null;

    [RelayCommand]
    private Task BackToGroup()
    {
        if (communityId is not Guid id) return Task.CompletedTask;
        return OpenAsync(id);
    }

    [RelayCommand]
    private void Back()
    {
        Watch(false);
        conversationId = null;
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
        try { await PullAsync(); }
        catch (CommunityClientException) { Status = T("groupFailed"); }
        catch (AccountClientException ex) { FailSession(ex); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Log.Error("group poll", ex); }
        finally { Interlocked.Exchange(ref polling, 0); }
    }

    private GroupMessageRow Row(ChatMessageResponse message)
    {
        return new(message.MessageId, message.SenderName, message.Body, message.CreatedAt.ToLocalTime().ToString("dd.MM HH:mm"), message.SenderId == me);
    }

    private void FailSession(AccountClientException ex)
    {
        if (ex.Failure is AccountClientFailure.InvalidSession or AccountClientFailure.ReauthenticationRequired
            or AccountClientFailure.SessionChanged) ShowAccount();
        else Status = T("groupFailed");
    }

    private void ShowAccount()
    {
        conversationId = null;
        RestartTimer();
        NeedAccount = true;
        HasHome = false;
        IsEmpty = false;
        Status = "";
        RaiseList();
    }

    private void RaiseList() => OnPropertyChanged(nameof(ShowList));
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

public sealed class GroupMessageRow(Guid id, string author, string body, string when, bool mine)
{
    public Guid Id { get; } = id;
    public string Author { get; } = author;
    public string Body { get; } = body;
    public string When { get; } = when;
    public bool Mine { get; } = mine;
}
