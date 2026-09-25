using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Features.Chat;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    private Guid? groupConversationId;
    private Guid? selectedTopicId;
    private bool selectedGroupChannel;
    private readonly Dictionary<(Guid ConversationId, Guid? TopicId), string> channelDrafts = [];
    private sealed record BallotDraftState(string Question, string A, string B, string C, string D, string E, string F, string Days);
    private readonly Dictionary<string, BallotDraftState> ballotDrafts = [];
    private string? activeBallotDraftKey;
    private bool loadingBallotDraft;
    private Guid? pendingCloseBallotId;
    private string? pendingCloseChannelKey;

    public ObservableCollection<GroupChannelRow> Channels { get; } = [];
    public ObservableCollection<GroupBallotRow> Ballots { get; } = [];
    [ObservableProperty] private bool canManageChannels;
    [ObservableProperty] private bool showBallots;
    [ObservableProperty] private GroupChannelRow? selectedChannel;
    [ObservableProperty] private string channelTitle = "";
    [ObservableProperty] private string channelIcon = "💬";
    [ObservableProperty] private string channelDescription = "";
    [ObservableProperty] private bool channelPinned;
    [ObservableProperty] private GroupChannelAccentChoice newChannelAccent = AccentChoices[0];
    [ObservableProperty] private GroupChannelPolicyChoice newChannelPolicy = PolicyChoices[0];
    [ObservableProperty] private string renameTitle = "";
    [ObservableProperty] private string renameIcon = "";
    [ObservableProperty] private string renameDescription = "";
    [ObservableProperty] private bool renamePinned;
    [ObservableProperty] private GroupChannelAccentChoice renameAccent = AccentChoices[0];
    [ObservableProperty] private GroupChannelPolicyChoice renamePolicy = PolicyChoices[0];
    [ObservableProperty] private bool channelEditConflict;
    private ChannelMetadata? renameBaseline;
    [ObservableProperty] private bool confirmDeleteChannel;
    [ObservableProperty] private bool canOpenBallot;
    [ObservableProperty] private string ballotQuestion = "";
    [ObservableProperty] private string ballotOptionA = "";
    [ObservableProperty] private string ballotOptionB = "";
    [ObservableProperty] private string ballotOptionC = "";
    [ObservableProperty] private string ballotOptionD = "";
    [ObservableProperty] private string ballotOptionE = "";
    [ObservableProperty] private string ballotOptionF = "";
    [ObservableProperty] private string ballotDays = "3";
    [ObservableProperty] private string pendingCloseQuestion = "";

    public static IReadOnlyList<GroupChannelAccentChoice> AccentChoices { get; } =
    [
        new("default", "По умолчанию"), new("blue", "Синий"), new("green", "Зелёный"),
        new("purple", "Фиолетовый"), new("orange", "Оранжевый"), new("red", "Красный")
    ];
    public static IReadOnlyList<GroupChannelPolicyChoice> PolicyChoices { get; } =
    [new("all", "Пишут все"), new("managers", "Пишут управляющие")];
    public IReadOnlyList<GroupChannelAccentChoice> ChannelAccents => AccentChoices;
    public IReadOnlyList<GroupChannelPolicyChoice> ChannelPolicies => PolicyChoices;

    public bool ShowMessages => !ShowBallots;
    public string DeleteConfirmationText => SelectedChannel?.Kind == "ballots"
        ? "Удалить канал? Голосования сохранятся в общем списке."
        : "Удалить канал и все его сообщения?";
    public bool CanPostChannel => IsDirect || SelectedChannel?.CanPost == true;
    public bool ShowComposer => !ShowBallots && CanPostChannel;
    public bool IsRestrictedChat => !ShowBallots && !IsDirect && SelectedChannel?.CanPost == false;
    public bool CanCreateBallot => ShowBallots && SelectedChannel?.Kind == "ballots"
        && (SelectedChannel.WritePolicy == "all" || CanManageChannels);
    public bool CanManageSelectedChannel => CanManageChannels && SelectedChannel is { TopicId: not null, CanDelete: true };
    public bool CanSaveChannelEdit => CanManageSelectedChannel && !ChannelEditConflict;
    public bool HasPendingCloseBallot => pendingCloseBallotId is not null;
    private string? TopicFilter => selectedGroupChannel ? selectedTopicId?.ToString("D") ?? "general" : null;
    private (Guid, Guid?) DraftKey(Guid id) => (id, selectedGroupChannel ? selectedTopicId : null);

    partial void OnCanManageChannelsChanged(bool value)
    {
        if (!value) ShowChannelManagement = false;
        OnPropertyChanged(nameof(CanManageSelectedChannel));
        OnPropertyChanged(nameof(ShowSelectedChannelManagement));
        OnPropertyChanged(nameof(CanSaveChannelEdit));
        OnPropertyChanged(nameof(CanCreateBallot));
    }
    partial void OnChannelEditConflictChanged(bool value) => OnPropertyChanged(nameof(CanSaveChannelEdit));
    partial void OnBallotQuestionChanged(string value) => SaveBallotDraft();
    partial void OnBallotOptionAChanged(string value) => SaveBallotDraft();
    partial void OnBallotOptionBChanged(string value) => SaveBallotDraft();
    partial void OnBallotOptionCChanged(string value) => SaveBallotDraft();
    partial void OnBallotOptionDChanged(string value) => SaveBallotDraft();
    partial void OnBallotOptionEChanged(string value) => SaveBallotDraft();
    partial void OnBallotOptionFChanged(string value) => SaveBallotDraft();
    partial void OnBallotDaysChanged(string value) => SaveBallotDraft();

    private BallotDraftState CurrentBallotDraft() => new(BallotQuestion, BallotOptionA, BallotOptionB,
        BallotOptionC, BallotOptionD, BallotOptionE, BallotOptionF, BallotDays);

    private void SaveBallotDraft()
    {
        if (!loadingBallotDraft && activeBallotDraftKey is { } key) ballotDrafts[key] = CurrentBallotDraft();
    }

    private void SelectBallotDraft(GroupChannelRow? channel)
    {
        activeBallotDraftKey = channel?.Kind == "ballots" && communityId is Guid community
            ? community.ToString("D") + ":" + channel.Key : null;
        if (activeBallotDraftKey is { } key) LoadBallotDraft(key);
        CancelCloseBallot();
    }

    private void LoadBallotDraft(string key)
    {
        var state = ballotDrafts.GetValueOrDefault(key) ?? new("", "", "", "", "", "", "", "3");
        loadingBallotDraft = true;
        try
        {
            BallotQuestion = state.Question;
            BallotOptionA = state.A;
            BallotOptionB = state.B;
            BallotOptionC = state.C;
            BallotOptionD = state.D;
            BallotOptionE = state.E;
            BallotOptionF = state.F;
            BallotDays = state.Days;
        }
        finally { loadingBallotDraft = false; }
    }

    private void ClearPublishedDraft(string? key, BallotDraftState submitted)
    {
        if (key is null || !ballotDrafts.TryGetValue(key, out var stored) || stored != submitted) return;
        ballotDrafts.Remove(key);
        if (activeBallotDraftKey == key) { LoadBallotDraft(key); ShowBallotComposer = false; }
    }

    private void AskCloseBallot(Guid ballotId, string question)
    {
        if (!ShowBallots || SelectedChannel is null) return;
        pendingCloseBallotId = ballotId;
        pendingCloseChannelKey = communityId?.ToString("D") + ":" + SelectedChannel.Key;
        PendingCloseQuestion = question;
        OnPropertyChanged(nameof(HasPendingCloseBallot));
    }

    [RelayCommand]
    private void CancelCloseBallot()
    {
        pendingCloseBallotId = null;
        pendingCloseChannelKey = null;
        PendingCloseQuestion = "";
        OnPropertyChanged(nameof(HasPendingCloseBallot));
    }

    [RelayCommand]
    private Task ConfirmCloseBallot()
    {
        if (pendingCloseBallotId is not Guid ballotId || SelectedChannel is null ||
            pendingCloseChannelKey != communityId?.ToString("D") + ":" + SelectedChannel.Key)
        { CancelCloseBallot(); return Task.CompletedTask; }
        CancelCloseBallot();
        return BallotActionAsync((api, token, community, ct) => api.CloseBallotAsync(token, community, ballotId, ct));
    }
    partial void OnIsDirectChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGroupContext));
        foreach (var channel in Channels) channel.IsSelected = !value && channel == SelectedChannel;
        OnPropertyChanged(nameof(HasUnreadChannel));
        OnPropertyChanged(nameof(CanPostChannel));
        OnPropertyChanged(nameof(ShowComposer));
        OnPropertyChanged(nameof(IsRestrictedChat));
        OnPropertyChanged(nameof(CanAttachMedia));
        SendCommand.NotifyCanExecuteChanged();
        StartRecordingCommand.NotifyCanExecuteChanged();
    }
    partial void OnShowBallotsChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGroupContext));
        OnPropertyChanged(nameof(ShowMessages));
        OnPropertyChanged(nameof(ShowComposer));
        OnPropertyChanged(nameof(IsRestrictedChat));
        OnPropertyChanged(nameof(CanCreateBallot));
        OnPropertyChanged(nameof(CanAttachMedia));
        SendCommand.NotifyCanExecuteChanged();
        StartRecordingCommand.NotifyCanExecuteChanged();
    }
    partial void OnSelectedChannelChanged(GroupChannelRow? value)
    {
        foreach (var channel in Channels) channel.IsSelected = !IsDirect && channel == value;
        OnPropertyChanged(nameof(HasUnreadChannel));
        ReloadChannelEditor();
        ConfirmDeleteChannel = false;
        OnPropertyChanged(nameof(CanManageSelectedChannel));
        OnPropertyChanged(nameof(ShowSelectedChannelManagement));
        OnPropertyChanged(nameof(CanSaveChannelEdit));
        OnPropertyChanged(nameof(DeleteConfirmationText));
        OnPropertyChanged(nameof(CanPostChannel));
        OnPropertyChanged(nameof(ShowComposer));
        OnPropertyChanged(nameof(IsRestrictedChat));
        OnPropertyChanged(nameof(CanCreateBallot));
        OnPropertyChanged(nameof(CanAttachMedia));
        SendCommand.NotifyCanExecuteChanged();
        StartRecordingCommand.NotifyCanExecuteChanged();
    }

    private sealed record ChannelMetadata(string Title, string Icon, string Description, string Accent, bool Pinned, string WritePolicy);
    private static ChannelMetadata Metadata(GroupChannelRow row) => new(row.Title, row.Icon, row.Description, row.Accent, row.Pinned, row.WritePolicy);
    private ChannelMetadata EditorMetadata() => new(RenameTitle, RenameIcon, RenameDescription, RenameAccent.Code, RenamePinned, RenamePolicy.Code);

    [RelayCommand]
    private void ReloadChannelEditor()
    {
        var value = SelectedChannel;
        renameBaseline = value is null ? null : Metadata(value);
        RenameTitle = value?.Title ?? "";
        RenameIcon = value?.Icon ?? "";
        RenameDescription = value?.Description ?? "";
        RenamePinned = value?.Pinned ?? false;
        RenameAccent = AccentChoices.FirstOrDefault(choice => choice.Code == value?.Accent) ?? AccentChoices[0];
        RenamePolicy = PolicyChoices.FirstOrDefault(choice => choice.Code == value?.WritePolicy) ?? PolicyChoices[0];
        ChannelEditConflict = false;
    }

    private void ReconcileChannelEditor()
    {
        if (SelectedChannel is not { } selected || renameBaseline is not { } baseline) return;
        var latest = Metadata(selected);
        if (latest == baseline) return;
        if (EditorMetadata() == baseline) ReloadChannelEditor();
        else ChannelEditConflict = true;
    }

    private async Task LoadChannelsAsync(string token, Guid community, Guid groupChat, int ticket, CancellationToken ct)
    {
        GroupTopicListResponse list;
        try { list = await Api!.TopicsAsync(token, community, ct); }
        catch (CommunityClientException)
        {
            list = new([new GroupTopicResponse(null, "Общий", "💬", null, null, null, 0, false)], false);
        }
        if (ct.IsCancellationRequested || navigationGeneration != ticket || communityId != community) return;
        groupConversationId = groupChat;
        ApplyChannels(list);
    }

    private void ApplyChannels(GroupTopicListResponse list)
    {
        var selected = SelectedChannel;
        var old = Channels.ToDictionary(row => row.Key);
        IReadOnlyList<GroupTopicResponse> source = list.Topics.Count == 0
            ? [new GroupTopicResponse(null, "Общий", "💬", null, null, null, 0, false)]
            : list.Topics;
        var general = source.FirstOrDefault(item => item.TopicId is null && item.Kind == "chat")
            ?? new GroupTopicResponse(null, "Общий", "💬", null, null, null, 0, false);
        var allBallots = new GroupTopicResponse(null, "Все голосования", "🗳", null, null, null, 0, false, "ballots");
        var ordered = new List<(GroupTopicResponse Item, bool GlobalBallots)> { (general, false), (allBallots, true) };
        ordered.AddRange(source.Where(item => item.TopicId is not null)
            .OrderBy(item => item.Pinned ? 0 : 1).Select(item => (item, false)));
        for (var index = 0; index < ordered.Count; index++)
        {
            var (item, globalBallots) = ordered[index];
            var key = globalBallots ? "global-ballots" : item.TopicId?.ToString("D") ?? "general";
            if (!old.TryGetValue(key, out var row))
            {
                GroupChannelRow created = null!;
                created = new(item, new RelayCommand(() => _ = OpenChannelAsync(created)), globalBallots);
                row = created;
                Channels.Insert(index, row);
            }
            else
            {
                row.Update(item);
                var position = Channels.IndexOf(row);
                if (position != index) Channels.Move(position, index);
            }
        }
        while (Channels.Count > ordered.Count) Channels.RemoveAt(Channels.Count - 1);
        RefreshGroupContext();
        RefreshChannelBrowse();
        CanManageChannels = list.CanManageChannels;
        if (selected is null || !Channels.Contains(selected)) SelectedChannel = Channels[0];
        else
        {
            ReconcileChannelEditor();
            if (!IsDirect) ChatTitle = selected.Title;
        }
        OnPropertyChanged(nameof(CanManageSelectedChannel));
        OnPropertyChanged(nameof(ShowSelectedChannelManagement));
        OnPropertyChanged(nameof(CanSaveChannelEdit));
        OnPropertyChanged(nameof(CanPostChannel));
        OnPropertyChanged(nameof(ShowComposer));
        OnPropertyChanged(nameof(IsRestrictedChat));
        OnPropertyChanged(nameof(CanCreateBallot));
        OnPropertyChanged(nameof(CanAttachMedia));
        SendCommand.NotifyCanExecuteChanged();
        StartRecordingCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshChannelsAsync(Guid id, int ticket)
    {
        if (communityId is not Guid community || groupConversationId is null || Api is null || Access is null) return;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrWhiteSpace(token) || !operation.IsCurrent || !CurrentChat(id, ticket)) return;
            var list = await Api.TopicsAsync(token, community, operation.Token);
            if (operation.IsCurrent && CurrentChat(id, ticket) && communityId == community)
            {
                var previous = SelectedChannel;
                ApplyChannels(list);
                if (!IsDirect && previous is not null && !Channels.Contains(previous))
                    await OpenChannelAsync(Channels[0]);
            }
        }
        catch (CommunityClientException) { }
        catch (AccountClientException ex) when (operation.IsCurrent && CurrentChat(id, ticket)) { FailSession(ex); }
        catch (OperationCanceledException) { }
    }

    private Task OpenChannelAsync(GroupChannelRow row)
    {
        if (groupConversationId is not Guid id || !Channels.Contains(row)) return Task.CompletedTask;
        SelectedChannel = row;
        return OpenConversationAsync(id, row.Title, false, row);
    }

    [RelayCommand]
    private async Task CreateChannel(string? kind)
    {
        if (!CanManageChannels || kind is not ("chat" or "ballots") || communityId is not Guid id || Api is null || Access is null) return;
        var ticket = navigationGeneration;
        using var operation = App.Work.Enter();
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrWhiteSpace(token)) { ShowAccount(); return; }
            var title = ChannelTitle.Trim();
            var list = await Api.CreateTopicAsync(token, id,
                new GroupTopicRequest(title, ChannelIcon.Trim(), kind, ChannelDescription.Trim(), NewChannelAccent.Code, ChannelPinned, NewChannelPolicy.Code), operation.Token);
            if (!operation.IsCurrent || communityId != id) return;
            ApplyChannels(list);
            if (ticket != navigationGeneration) return;
            ChannelTitle = "";
            ChannelDescription = "";
            ChannelPinned = false;
            NewChannelAccent = AccentChoices[0];
            NewChannelPolicy = PolicyChoices[0];
            var created = Channels.FirstOrDefault(row => row.Kind == kind && row.Title == title);
            if (created is not null) await OpenChannelAsync(created);
            Status = "";
        }
        catch (Exception ex) when (ex is CommunityClientException or ArgumentException)
        { if (operation.IsCurrent) Status = "Не удалось создать канал. Проверьте название и значок."; }
        catch (AccountClientException ex) when (operation.IsCurrent) { FailSession(ex); }
        catch (OperationCanceledException) { }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    [RelayCommand]
    private async Task RenameChannel()
    {
        if (!CanSaveChannelEdit || SelectedChannel?.TopicId is not Guid topic || communityId is not Guid id || Api is null || Access is null) return;
        var ticket = navigationGeneration;
        var kind = SelectedChannel.Kind;
        var requested = EditorMetadata();
        var expected = renameBaseline;
        using var operation = App.Work.Enter();
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrWhiteSpace(token)) { ShowAccount(); return; }
            if (!operation.IsCurrent || ticket != navigationGeneration || SelectedChannel?.TopicId != topic) return;
            var fresh = await Api.TopicsAsync(token, id, operation.Token);
            if (!operation.IsCurrent || ticket != navigationGeneration || SelectedChannel?.TopicId != topic) return;
            var latest = fresh.Topics.FirstOrDefault(row => row.TopicId == topic);
            if (latest is null || expected is null ||
                new ChannelMetadata(latest.Title, latest.Icon, latest.Description, latest.Accent, latest.Pinned, latest.WritePolicy) != expected)
            {
                if (operation.IsCurrent && communityId == id) { ApplyChannels(fresh); ChannelEditConflict = true; }
                return;
            }
            var list = await Api.RenameTopicAsync(token, id, topic,
                new GroupTopicRequest(requested.Title.Trim(), requested.Icon.Trim(), kind, requested.Description.Trim(), requested.Accent,
                    requested.Pinned, requested.WritePolicy), operation.Token);
            if (!operation.IsCurrent || communityId != id || ticket != navigationGeneration || SelectedChannel?.TopicId != topic) return;
            ApplyChannels(list);
            ReloadChannelEditor();
            if (SelectedChannel?.TopicId == topic) ChatTitle = SelectedChannel.Title;
            Status = "";
        }
        catch (Exception ex) when (ex is CommunityClientException or ArgumentException)
        { if (operation.IsCurrent) Status = "Не удалось изменить канал."; }
        catch (AccountClientException ex) when (operation.IsCurrent) { FailSession(ex); }
        catch (OperationCanceledException) { }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    [RelayCommand]
    private void AskDeleteChannel() => ConfirmDeleteChannel = CanManageSelectedChannel;

    [RelayCommand]
    private void CancelDeleteChannel() => ConfirmDeleteChannel = false;

    [RelayCommand]
    private async Task DeleteChannel()
    {
        if (!ConfirmDeleteChannel || !CanManageSelectedChannel || SelectedChannel?.TopicId is not Guid topic || communityId is not Guid id || Api is null || Access is null) return;
        var ticket = navigationGeneration;
        using var operation = App.Work.Enter();
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrWhiteSpace(token)) { ShowAccount(); return; }
            var list = await Api.DeleteTopicAsync(token, id, topic, operation.Token);
            if (!operation.IsCurrent || communityId != id) return;
            ApplyChannels(list);
            ConfirmDeleteChannel = false;
            if (ticket == navigationGeneration) await OpenChannelAsync(Channels[0]);
            Status = "";
        }
        catch (CommunityClientException) { if (operation.IsCurrent) Status = "Не удалось удалить канал."; }
        catch (AccountClientException ex) when (operation.IsCurrent) { FailSession(ex); }
        catch (OperationCanceledException) { }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    private async Task LoadBallotsAsync(Guid id, int ticket)
    {
        if (!ShowBallots || SelectedChannel?.Kind != "ballots" || communityId is not Guid community || Api is null || Access is null) return;
        var serial = ++ballotRequestSerial;
        BallotLoading = !BallotLoaded;
        BallotLoadFailed = false;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (!operation.IsCurrent || !CurrentChat(id, ticket) || !ShowBallots) return;
            if (string.IsNullOrWhiteSpace(token)) { ShowAccount(); return; }
            var board = await Api.BallotsAsync(token, community, selectedTopicId, operation.Token);
            if (serial == ballotRequestSerial && operation.IsCurrent && CurrentChat(id, ticket) && ShowBallots)
            {
                ApplyBallots(board);
                BallotLoaded = true;
            }
        }
        catch (CommunityClientException)
        {
            if (serial != ballotRequestSerial || !CurrentChat(id, ticket)) return;
            BallotLoadFailed = true;
            throw;
        }
        finally
        {
            if (serial == ballotRequestSerial && CurrentChat(id, ticket)) BallotLoading = false;
        }
    }

    private void ApplyBallots(BallotBoardResponse board)
    {
        BallotFeedback = "";
        CanOpenBallot = board.CanOpen;
        if (pendingCloseBallotId is Guid closing && !board.Ballots.Any(ballot => ballot.BallotId == closing && ballot.Status != "closed"))
            CancelCloseBallot();
        Ballots.Clear();
        foreach (var ballot in board.Ballots)
            Ballots.Add(new GroupBallotRow(ballot, board.CanClose,
                id => BallotActionAsync((api, token, community, ct) => api.SupportBallotAsync(token, community, id, ct)),
                (id, option) => BallotActionAsync((api, token, community, ct) => api.VoteBallotAsync(token, community, id, new VoteRequest(option), ct)),
                id => { AskCloseBallot(id, ballot.Question); return Task.CompletedTask; },
                CopyBallotSummaryAsync));
    }

    private async Task BallotActionAsync(Func<CommunityHttpClient, string, Guid, CancellationToken, Task<BallotBoardResponse>> action, Action? accepted = null)
    {
        if (!ShowBallots || communityId is not Guid community || conversationId is not Guid id || Api is null || Access is null) return;
        var ticket = navigationGeneration;
        using var operation = App.Work.Enter();
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrWhiteSpace(token)) { ShowAccount(); return; }
            await action(Api, token, community, operation.Token);
            accepted?.Invoke();
            if (operation.IsCurrent && CurrentChat(id, ticket)) await LoadBallotsAsync(id, ticket);
            if (operation.IsCurrent && CurrentChat(id, ticket)) Status = "";
        }
        catch (CommunityClientException) { if (operation.IsCurrent && CurrentChat(id, ticket)) Status = "Не удалось обновить голосование."; }
        catch (AccountClientException ex) when (operation.IsCurrent && CurrentChat(id, ticket)) { FailSession(ex); }
        catch (OperationCanceledException) { }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    [RelayCommand]
    private Task OpenBallot() => PublishBallotAsync(true);
    [RelayCommand]
    private Task ProposeBallot() => PublishBallotAsync(false);

    private Task PublishBallotAsync(bool headman)
    {
        if (!CanCreateBallot || headman && !CanOpenBallot) return Task.CompletedTask;
        if (!int.TryParse(BallotDays, out var days) || days is < 1 or > 14 || string.IsNullOrWhiteSpace(BallotQuestion)
            || string.IsNullOrWhiteSpace(BallotOptionA) || string.IsNullOrWhiteSpace(BallotOptionB))
        { Status = "Укажите вопрос, два варианта и срок от 1 до 14 дней."; return Task.CompletedTask; }
        BallotDraftRequest request;
        var submitted = CurrentBallotDraft();
        var draftKey = activeBallotDraftKey;
        var topic = selectedTopicId;
        var options = new[] { BallotOptionA, BallotOptionB, BallotOptionC, BallotOptionD, BallotOptionE, BallotOptionF }
            .Select(option => option.Trim()).Where(option => option.Length > 0).ToArray();
        try { request = new(BallotQuestion.Trim(), options, days, topic); }
        catch (ArgumentException) { Status = "Проверьте вопрос и варианты голосования."; return Task.CompletedTask; }
        return BallotActionAsync((api, token, community, ct) => headman
            ? api.OpenHeadmanBallotAsync(token, community, request, ct)
            : api.ProposeBallotAsync(token, community, request, ct),
            () => ClearPublishedDraft(draftKey, submitted));
    }
}

public sealed class GroupChannelRow(GroupTopicResponse initial, IRelayCommand open, bool globalBallots = false) : ObservableObject
{
    private GroupTopicResponse row = initial;
    private bool isSelected;
    public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
    internal string Key => IsGlobalBallots ? "global-ballots" : row.TopicId?.ToString("D") ?? "general";
    public bool IsGlobalBallots { get; } = globalBallots;
    public Guid? TopicId => row.TopicId;
    public string Kind => row.Kind;
    public string Title => row.Title;
    public string Icon => row.Icon;
    public string Description => row.Description;
    public string Accent => row.Accent;
    public bool Pinned => row.Pinned;
    public string PinMark => row.Pinned ? "📌" : "";
    public string WritePolicy => row.WritePolicy;
    public bool CanPost => row.CanPost;
    public IBrush AccentBrush => new SolidColorBrush(Color.Parse(row.Accent switch
    {
        "blue" => "#3d5a80", "green" => "#3d6b4f", "purple" => "#6b3d5a", "orange" => "#8a5a2a", "red" => "#6b4030",
        _ => DefaultAccent(row.Title)
    }));
    private static string DefaultAccent(string title)
    {
        string[] paints = ["#3d6b4f", "#3d5a80", "#8a5a2a", "#6b3d5a", "#3d5a6b", "#5a4a3d", "#6b4030", "#2f5d50"];
        var hash = 0;
        foreach (var ch in title) hash = (hash + ch) % paints.Length;
        return paints[hash];
    }
    public string Preview => IsGlobalBallots ? "Опросы и голосования всех каналов"
        : row.Kind == "ballots"
        ? (row.LastBody is { Length: > 0 } ? row.LastBody + " · " : "") + $"Активных голосований: {row.ActiveBallots}"
        : row.LastBody ?? "Пока пусто";
    public string ActivityText
    {
        get
        {
            if (row.Kind != "chat" || row.LastAt is not { } at) return "";
            var local = at.ToLocalTime();
            return (string.IsNullOrWhiteSpace(row.LastAuthor) ? "" : row.LastAuthor + " · ")
                + local.ToString(local.Year == DateTime.Now.Year ? "dd.MM HH:mm" : "dd.MM.yyyy HH:mm");
        }
    }
    public string Unread => UnreadBadge.Label(row.Unread);
    public int UnreadCount => row.Unread;
    public int ActiveBallots => row.ActiveBallots;
    public string UnreadDescription => UnreadBadge.Description(row.Unread);
    public bool CanDelete => row.CanDelete;
    public IRelayCommand OpenCommand { get; } = open;
    internal void Update(GroupTopicResponse next)
    {
        row = next;
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Accent));
        OnPropertyChanged(nameof(Pinned));
        OnPropertyChanged(nameof(PinMark));
        OnPropertyChanged(nameof(WritePolicy));
        OnPropertyChanged(nameof(CanPost));
        OnPropertyChanged(nameof(AccentBrush));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(ActiveBallots));
        OnPropertyChanged(nameof(ActivityText));
        OnPropertyChanged(nameof(Unread));
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(UnreadDescription));
        OnPropertyChanged(nameof(CanDelete));
    }
}

public sealed record GroupChannelAccentChoice(string Code, string Label);
public sealed record GroupChannelPolicyChoice(string Code, string Label);

public sealed class GroupBallotRow
{
    public GroupBallotRow(BallotResponse ballot, bool canClose, Func<Guid, Task> support, Func<Guid, Guid, Task> vote,
        Func<Guid, Task> close, Func<GroupBallotRow, Task>? copy = null)
    {
        BallotId = ballot.BallotId;
        Question = ballot.Question;
        StatusCode = ballot.Status;
        DeadlineAt = ballot.DeadlineAt;
        Origin = ballot.Origin switch { "system" => "Система", "headman" => "Староста", "collective" => "Общее предложение", _ => ballot.Origin };
        Status = ballot.Status switch { "collecting" => "Сбор поддержки", "open" => "Идёт голосование",
            "closed" => "Завершено", _ => ballot.Status };
        var localDeadline = ballot.DeadlineAt.ToLocalTime();
        Deadline = "До " + localDeadline.ToString(localDeadline.Year == DateTime.Now.Year ? "dd.MM HH:mm" : "dd.MM.yyyy HH:mm");
        DeadlineHint = GroupBallotBrowse.Urgency(ballot.Status, ballot.DeadlineAt, DateTimeOffset.UtcNow);
        Supporters = $"Поддержали {ballot.Supporters} из {ballot.SupportersNeeded}";
        IsCollecting = ballot.Status == "collecting";
        IsEffect = !string.IsNullOrEmpty(ballot.Effect);
        Outcome = ballot.Outcome switch { "accepted" => "Изменение принято", "rejected" => "Изменение отклонено", "skipped" => "Изменение не применено", _ => ballot.Outcome };
        CanSupport = ballot.Status == "collecting" && !ballot.Supported;
        AlreadySupported = ballot.Status == "collecting" && ballot.Supported;
        CanClose = canClose && ballot.Status != "closed" && string.IsNullOrEmpty(ballot.Effect);
        var totalVotes = ballot.Options.Sum(option => (long)option.Votes);
        TotalVotes = totalVotes;
        Options = ballot.Options.Select(option => new GroupBallotOptionRow(option, totalVotes,
            ballot.Status == "open", () => vote(ballot.BallotId, option.OptionId))).ToArray();
        SupportCommand = new AsyncRelayCommand(() => support(ballot.BallotId));
        CloseCommand = new AsyncRelayCommand(() => close(ballot.BallotId));
        CopyCommand = copy is null ? null : new AsyncRelayCommand(() => copy(this));
    }
    public Guid BallotId { get; }
    public string Question { get; }
    public string StatusCode { get; }
    public DateTimeOffset DeadlineAt { get; }
    public string Origin { get; }
    public string Status { get; }
    public string Deadline { get; }
    public string DeadlineHint { get; }
    public bool HasDeadlineHint => DeadlineHint.Length > 0;
    public string Supporters { get; }
    public string Outcome { get; }
    public long TotalVotes { get; }
    public string VoteShareCaption => TotalVotes == 0 ? "Голосов пока нет. Доли считаются от поданных голосов."
        : $"Подано голосов: {TotalVotes}. Доли считаются от поданных голосов.";
    public bool IsCollecting { get; }
    public bool IsEffect { get; }
    public bool CanSupport { get; }
    public bool AlreadySupported { get; }
    public bool CanClose { get; }
    public IReadOnlyList<GroupBallotOptionRow> Options { get; }
    public IAsyncRelayCommand SupportCommand { get; }
    public IAsyncRelayCommand CloseCommand { get; }
    public IAsyncRelayCommand? CopyCommand { get; }
    public bool CanCopySummary => CopyCommand is not null;
}

public sealed class GroupBallotOptionRow(BallotOptionResponse option, long totalVotes, bool canVote, Func<Task> vote)
{
    public string Label => option.Label + " · " + option.Votes + (option.Chosen ? " ✓" : "");
    public string Text => option.Label;
    public int Votes => option.Votes;
    public int Percent => GroupBallotBrowse.Percent(option.Votes, totalVotes);
    public string VoteCountCaption => $"Голосов: {Votes} · {Percent}% голосов";
    public string ChoiceCaption => option.Chosen ? "Ваш выбор" : "";
    public bool IsChosen => option.Chosen;
    public string PercentAccessible => $"{Text}: голосов {Votes}, {Percent}% от поданных голосов";
    public bool CanVote { get; } = canVote;
    public IAsyncRelayCommand VoteCommand { get; } = new AsyncRelayCommand(vote);
}
