using System.Net;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Features.Groups;
using Vograph.Desktop.Services;
using Zapara.Contracts.Communities;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class GroupChannelViewModelTests
{
    private static readonly Guid GroupChat = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid ChatTopic = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid BallotTopic = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid NewTopic = Guid.Parse("88888888-8888-4888-8888-888888888888");

    [AvaloniaFact]
    public async Task GroupContextKeepsItsCollapsedStateAcrossChannelsAndHidesOnBallotBoards()
    {
        using var fixture = new Fixture(canManage: false);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        await Waits.Until(() => vm.ShowGroupContext, "group context from active ballot channel");
        Assert.Equal("Активных голосований: 1", vm.ContextBallotsText);

        vm.ToggleContextCommand.Execute(null);
        Assert.False(vm.ContextExpanded);
        vm.Channels.Single(row => row.TopicId == ChatTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.SelectedChannel?.TopicId == ChatTopic, "chat channel selected");
        Assert.True(vm.ShowGroupContext);
        Assert.False(vm.ContextExpanded);

        vm.Channels.Single(row => row.TopicId == BallotTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ShowBallots, "ballot channel selected");
        Assert.False(vm.ShowGroupContext);
    }

    [AvaloniaFact]
    public async Task Ballot_browse_reapplies_search_and_status_after_live_board_replacement()
    {
        using var fixture = new Fixture(canManage: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.Channels.Single(row => row.TopicId == BallotTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ShowBallots && vm.Ballots.Count == 1, "ballot channel opened");

        vm.BallotSearch = "новый";
        Assert.True(vm.NoBallotMatches);
        vm.BallotStatusIndex = 2;
        fixture.SetBoard(new BallotBoardResponse(false, true, false, 3, 2, [
            new(PollId, "Новый вопрос", "headman", "open", CommunityClientTestSupport.Now.AddDays(1),
                0, 2, false, [new(OptionYes, "Да", 1, false), new(OptionNo, "Нет", 7, false)], "", "", BallotTopic)
        ]));
        await vm.Ballots[0].Options[0].VoteCommand.ExecuteAsync(null);

        Assert.Equal("Новый вопрос", Assert.Single(vm.FilteredBallots).Question);
        Assert.Equal("Показано 1 из 1 на текущей доске", vm.BallotResultCount);
        Assert.Equal(13, vm.FilteredBallots[0].Options[0].Percent);
        vm.BallotStatusIndex = 3;
        Assert.True(vm.NoBallotMatches);
        vm.ResetBallotFiltersCommand.Execute(null);
        Assert.Single(vm.FilteredBallots);
    }

    [AvaloniaFact]
    public async Task Ballot_loading_error_is_distinct_from_empty_and_retry_recovers()
    {
        using var fixture = new Fixture(canManage: false);
        fixture.FailBallotGet = true;
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.Channels.Single(row => row.TopicId == BallotTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ShowBallots && vm.BallotLoadFailed, "ballot load failed");
        Assert.False(vm.NoBallots);
        Assert.False(vm.BallotLoading);

        fixture.FailBallotGet = false;
        await vm.RetryBallotsCommand.ExecuteAsync(null);
        Assert.False(vm.BallotLoadFailed);
        Assert.True(vm.BallotLoaded);
        Assert.Single(vm.Ballots);
        Assert.Equal("", vm.Status);
    }

    [AvaloniaFact]
    public async Task Empty_ballot_board_is_not_a_loading_or_error_state()
    {
        using var fixture = new Fixture(canManage: false);
        fixture.SetBoard(new BallotBoardResponse(false, false, false, 3, 2, []));
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.Channels.Single(row => row.TopicId == BallotTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ShowBallots && vm.BallotLoaded, "empty ballot board loaded");
        Assert.True(vm.NoBallots);
        Assert.False(vm.BallotLoading);
        Assert.False(vm.BallotLoadFailed);
        vm.BallotSearch = "нет";
        Assert.False(vm.NoBallots);
        Assert.True(vm.NoBallotMatches);
    }

    [AvaloniaFact]
    public async Task Ballot_copy_feedback_and_failed_create_keep_in_memory_draft()
    {
        using var fixture = new Fixture(canManage: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.Channels.Single(row => row.TopicId == BallotTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ShowBallots && vm.Ballots.Count == 1, "ballot channel opened");

        string? copied = null;
        vm.SetClipboardWriter(value => { copied = value; return Task.CompletedTask; });
        await vm.Ballots[0].CopyCommand!.ExecuteAsync(null);
        Assert.Contains("Когда встречаемся?", copied);
        Assert.Contains("Завтра: 1 голос · 100% голосов", copied);
        Assert.Equal("Сводка скопирована.", vm.BallotFeedback);
        vm.SetClipboardWriter(_ => throw new InvalidOperationException("clipboard unavailable"));
        await vm.Ballots[0].CopyCommand!.ExecuteAsync(null);
        Assert.Equal("Не удалось скопировать сводку.", vm.BallotFeedback);

        vm.ToggleBallotComposerCommand.Execute(null);
        Assert.True(vm.ShowBallotComposer);
        vm.BallotQuestion = "Вопрос черновика";
        vm.BallotOptionA = "Да";
        vm.BallotOptionB = "Нет";
        fixture.FailBallotCreate = true;
        await vm.ProposeBallotCommand.ExecuteAsync(null);
        Assert.Equal("Вопрос черновика", vm.BallotQuestion);
        Assert.True(vm.ShowBallotComposer);
    }

    [AvaloniaFact]
    public async Task Next_unread_opens_a_real_channel_and_updates_selection()
    {
        using var fixture = new Fixture(canManage: false);
        fixture.SetUnread(ChatTopic, 2);
        var vm = fixture.Vm;
        await vm.ActivateAsync();

        Assert.True(vm.HasUnreadChannel);
        vm.NextUnreadChannelCommand.Execute(null);
        await Waits.Until(() => vm.SelectedChannel?.TopicId == ChatTopic, "next unread channel opened");
        Assert.True(vm.SelectedChannel?.IsSelected);
    }

    [AvaloniaFact]
    public async Task Switching_chat_channels_keeps_drafts_and_scopes_reads_and_sends()
    {
        using var fixture = new Fixture(canManage: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        Assert.Equal(4, vm.Channels.Count);
        Assert.Equal("Общий", vm.ChatTitle);
        vm.Draft = "Общий черновик";

        vm.Channels.Single(row => row.TopicId == ChatTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ChatTitle == "Расписание" && fixture.Requests.Any(path => path.EndsWith($"/messages?topic={ChatTopic:D}", StringComparison.Ordinal)), "chat topic opened");
        vm.Draft = "Сообщение в разделе";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.Equal(ChatTopic, fixture.SentTopic);
        Assert.Equal("Сообщение в разделе", fixture.SentBody);

        vm.Channels.Single(row => row.TopicId is null && row.Kind == "chat").OpenCommand.Execute(null);
        await Waits.Until(() => vm.ChatTitle == "Общий", "general chat reopened");
        Assert.Equal("Общий черновик", vm.Draft);
        Assert.Contains(fixture.Requests, path => path.EndsWith("/messages?topic=general", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Ballot_channel_has_cards_and_rejects_chat_composer()
    {
        using var fixture = new Fixture(canManage: false);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        Assert.False(vm.CanManageChannels);
        vm.Channels.Single(row => row.TopicId == BallotTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ShowBallots && vm.Ballots.Count == 1, "ballot channel opened");
        Assert.False(vm.SendCommand.CanExecute(null));
        Assert.False(vm.CanAttachMedia);
        Assert.Contains(fixture.Requests, path => path.EndsWith($"/ballots?topic={BallotTopic:D}", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Requests, path => path.EndsWith($"/messages?topic={BallotTopic:D}", StringComparison.Ordinal));

        await vm.Ballots[0].Options[0].VoteCommand.ExecuteAsync(null);
        Assert.Equal(OptionYes, fixture.VotedOption);
        Assert.True(fixture.Requests.Count(path => path.EndsWith($"/ballots?topic={BallotTopic:D}", StringComparison.Ordinal)) >= 2);
    }

    [AvaloniaFact]
    public async Task All_ballots_entry_reads_the_unfiltered_board()
    {
        using var fixture = new Fixture(canManage: false);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        var all = vm.Channels.Single(row => row.IsGlobalBallots);
        Assert.Equal("Все голосования", all.Title);
        all.OpenCommand.Execute(null);
        await Waits.Until(() => vm.ShowBallots && vm.Ballots.Count == 1, "all ballots opened");
        Assert.Contains(fixture.Requests, path => path.EndsWith($"/{CommunityId:D}/ballots", StringComparison.Ordinal));
        Assert.False(vm.CanManageSelectedChannel);
        vm.BallotQuestion = "Как прошла неделя?";
        vm.BallotOptionA = "Хорошо";
        vm.BallotOptionB = "Сложно";
        await vm.ProposeBallotCommand.ExecuteAsync(null);
        Assert.Null(fixture.CreatedBallotTopic);
        Assert.Contains(fixture.Requests, path => path.EndsWith($"/{CommunityId:D}/ballots/collective", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Only_a_manager_can_create_channels_from_the_desktop()
    {
        using var fixture = new Fixture(canManage: false);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.ChannelTitle = "Лабораторные";
        await vm.CreateChannelCommand.ExecuteAsync("chat");
        Assert.DoesNotContain(Enumerable.Range(0, fixture.Requests.Count), index =>
            fixture.Methods[index] == "POST" && fixture.Requests[index].Contains("/topics?", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Manager_can_create_rename_and_confirm_deletion_without_changing_channel_kind()
    {
        using var fixture = new Fixture(canManage: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.ChannelTitle = "Семинар";
        vm.ChannelIcon = "📚";
        vm.ChannelDescription = "Выбор даты встречи";
        vm.NewChannelAccent = vm.ChannelAccents.Single(choice => choice.Code == "purple");
        vm.ChannelPinned = true;
        vm.NewChannelPolicy = vm.ChannelPolicies.Single(choice => choice.Code == "managers");
        await vm.CreateChannelCommand.ExecuteAsync("ballots");
        Assert.Equal("ballots", fixture.CreatedKind);
        Assert.Equal(("Выбор даты встречи", "purple", true, "managers"), fixture.CreatedMetadata);
        Assert.Equal(NewTopic, vm.SelectedChannel?.TopicId);
        Assert.True(vm.CanManageSelectedChannel);

        vm.RenameTitle = "Практика";
        vm.RenameDescription = "Выбор времени практики";
        vm.RenameAccent = vm.ChannelAccents.Single(choice => choice.Code == "orange");
        vm.RenamePinned = false;
        vm.RenamePolicy = vm.ChannelPolicies.Single(choice => choice.Code == "all");
        await vm.RenameChannelCommand.ExecuteAsync(null);
        Assert.Equal("ballots", fixture.RenamedKind);
        Assert.Equal(("Выбор времени практики", "orange", false, "all"), fixture.RenamedMetadata);
        Assert.Equal("Практика", vm.SelectedChannel?.Title);
        Assert.Equal("Практика", vm.ChatTitle);

        vm.AskDeleteChannelCommand.Execute(null);
        Assert.True(vm.ConfirmDeleteChannel);
        await vm.DeleteChannelCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.DeleteCalls);
        Assert.Null(vm.SelectedChannel?.TopicId);
    }

    [AvaloniaFact]
    public async Task Channel_list_refreshes_while_an_open_chat_keeps_its_draft()
    {
        using var fixture = new Fixture(canManage: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.Draft = "Сохранить черновик";
        vm.Watch(true);
        fixture.AddExternalChannel();

        await Waits.Until(() => vm.Channels.Any(row => row.TopicId == NewTopic), "remote channel appeared", 6500);
        Assert.Equal("Сохранить черновик", vm.Draft);
        Assert.Null(vm.SelectedChannel?.TopicId);
    }

    [AvaloniaFact]
    public async Task Ballot_creation_sends_selected_channel_and_all_entered_options()
    {
        using var fixture = new Fixture(canManage: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.Channels.Single(row => row.TopicId == BallotTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ShowBallots && vm.Ballots.Count == 1, "ballot channel loaded");
        vm.BallotQuestion = "Когда встречаемся?";
        vm.BallotOptionA = "Завтра";
        vm.BallotOptionB = "В пятницу";
        vm.BallotOptionC = "В понедельник";
        vm.BallotDays = "4";

        await vm.OpenBallotCommand.ExecuteAsync(null);
        Assert.Equal(BallotTopic, fixture.CreatedBallotTopic);
        Assert.Equal(["Завтра", "В пятницу", "В понедельник"], fixture.CreatedOptions);
    }

    [AvaloniaFact]
    public async Task Headman_can_grant_and_revoke_channel_management_through_a_group_role()
    {
        using var fixture = new Fixture(canManage: true, headman: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        Assert.True(vm.IsHeadman);
        vm.TrustedRoleName = "Доверенный по каналам";
        await vm.CreateTrustedRoleCommand.ExecuteAsync(null);
        Assert.Equal("Доверенный по каналам", vm.SelectedTrustedRole?.Name);

        await vm.ToggleChannelPowerCommand.ExecuteAsync(null);
        Assert.True(vm.SelectedTrustedRole?.HasChannelsPower);
        vm.SelectedTrustCandidate = vm.TrustCandidates.Single(person => person.UserId == PromotedId);
        await vm.GrantTrustedCommand.ExecuteAsync(null);
        Assert.Single(vm.TrustedGrants);
        Assert.Equal(PromotedId, vm.TrustedGrants[0].UserId);

        await vm.TrustedGrants[0].RevokeCommand.ExecuteAsync(null);
        Assert.Empty(vm.TrustedGrants);
        Assert.Equal(["create", "power", "grant", "revoke"], fixture.TrustActions);
    }

    [AvaloniaFact]
    public async Task Restricted_chat_hides_composer_and_restricted_ballots_keep_voting()
    {
        using var fixture = new Fixture(canManage: false, restricted: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.Channels.Single(row => row.TopicId == ChatTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ChatTitle == "Расписание", "restricted chat opened");
        vm.Draft = "Нельзя отправлять";
        Assert.False(vm.ShowComposer);
        Assert.False(vm.SendCommand.CanExecute(null));
        Assert.False(vm.CanAttachMedia);

        vm.Channels.Single(row => row.TopicId == BallotTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ShowBallots && vm.Ballots.Count == 1, "restricted ballots opened");
        Assert.False(vm.CanCreateBallot);
        await vm.Ballots[0].Options[0].VoteCommand.ExecuteAsync(null);
        Assert.Equal(OptionYes, fixture.VotedOption);
    }

    [AvaloniaFact]
    public async Task Pinned_channels_sort_before_regular_channels_and_keep_metadata()
    {
        using var fixture = new Fixture(canManage: true);
        fixture.AddExternalChannel(pinned: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        Assert.Equal(["chat", "ballots", "chat", "chat", "ballots"], vm.Channels.Select(row => row.Kind).ToArray());
        Assert.Equal(NewTopic, vm.Channels[2].TopicId);
        var pinned = vm.Channels[2];
        Assert.True(pinned.Pinned);
        Assert.Equal("blue", pinned.Accent);
        Assert.Equal("Важные обновления", pinned.Description);
    }

    [AvaloniaFact]
    public async Task Late_send_to_one_topic_does_not_enter_another_topic_in_the_same_conversation()
    {
        using var fixture = new Fixture(canManage: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        fixture.SendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.SendRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.Draft = "Для общего";
        var sending = vm.SendCommand.ExecuteAsync(null);
        try
        {
            await fixture.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            vm.Channels.Single(row => row.TopicId == ChatTopic).OpenCommand.Execute(null);
            await Waits.Until(() => vm.ChatTitle == "Расписание", "other topic opened");
            vm.Draft = "Для расписания";
            fixture.SendRelease.TrySetResult(Payload(new ChatMessageResponse(PollId, GroupChat, UserId,
                "Аня", "Для общего", CommunityClientTestSupport.Now), HttpStatusCode.Created));
            await sending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal("Для расписания", vm.Draft);
            Assert.DoesNotContain(vm.Messages, row => row.Body == "Для общего");
        }
        finally { fixture.SendRelease.TrySetResult(Problem(503, "db_unavailable")); }
    }

    [AvaloniaFact]
    public async Task Periodic_topic_refresh_revokes_stale_composer_and_management_controls()
    {
        using var fixture = new Fixture(canManage: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.Channels.Single(row => row.TopicId == ChatTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ChatTitle == "Расписание", "chat topic opened");
        Assert.True(vm.ShowComposer);
        Assert.True(vm.CanManageChannels);
        vm.Watch(true);
        fixture.RevokeChannelAccess();

        await Waits.Until(() => !vm.ShowComposer && !vm.CanManageChannels, "channel permissions refreshed", 6500);
        vm.Draft = "Больше нельзя писать";
        Assert.False(vm.SendCommand.CanExecute(null));
        Assert.False(vm.CanAttachMedia);
    }

    [AvaloniaFact]
    public async Task Deleting_the_selected_channel_elsewhere_returns_to_general_chat()
    {
        using var fixture = new Fixture(canManage: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.Channels.Single(row => row.TopicId == ChatTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ChatTitle == "Расписание", "chat topic opened");
        vm.Watch(true);
        fixture.RemoveChannel(ChatTopic);

        await Waits.Until(() => vm.SelectedChannel?.Kind == "chat" && vm.SelectedChannel.TopicId is null
            && vm.ChatTitle == "Общий", "deleted topic left safely", 6500);
        Assert.Contains(fixture.Requests, path => path.EndsWith("/messages?topic=general", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Clean_channel_editor_tracks_remote_metadata_changes()
    {
        using var fixture = new Fixture(canManage: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.Channels.Single(row => row.TopicId == ChatTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ChatTitle == "Расписание", "chat topic opened");
        vm.Watch(true);
        fixture.ChangeChatMetadata("Новое описание", "red", true, "managers");

        await Waits.Until(() => vm.RenameDescription == "Новое описание", "editor metadata refreshed", 6500);
        Assert.Equal("red", vm.RenameAccent.Code);
        Assert.True(vm.RenamePinned);
        Assert.Equal("managers", vm.RenamePolicy.Code);
    }

    [AvaloniaFact]
    public async Task Dirty_channel_editor_blocks_a_stale_policy_write_until_reloaded()
    {
        using var fixture = new Fixture(canManage: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.Channels.Single(row => row.TopicId == ChatTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ChatTitle == "Расписание", "chat topic opened");
        vm.RenameDescription = "Мой несохранённый текст";
        vm.Watch(true);
        fixture.ChangeChatMetadata("Новое описание", "red", true, "managers");

        await Waits.Until(() => vm.ChannelEditConflict, "remote edit conflict detected", 6500);
        await vm.RenameChannelCommand.ExecuteAsync(null);
        Assert.DoesNotContain(Enumerable.Range(0, fixture.Requests.Count), index =>
            fixture.Methods[index] == "POST" && fixture.Requests[index].Contains($"/topics/{ChatTopic:D}", StringComparison.Ordinal));
        vm.ReloadChannelEditorCommand.Execute(null);
        Assert.False(vm.ChannelEditConflict);
        Assert.Equal("Новое описание", vm.RenameDescription);
        Assert.Equal("managers", vm.RenamePolicy.Code);
    }

    [AvaloniaFact]
    public async Task Ballot_draft_is_scoped_to_channel_and_clears_after_successful_publication()
    {
        using var fixture = new Fixture(canManage: true);
        fixture.AddExternalBallotChannel();
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.Channels.Single(row => row.TopicId == BallotTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ShowBallots, "first ballot channel opened");
        vm.BallotQuestion = "Вопрос А";
        vm.BallotOptionA = "Да";
        vm.BallotOptionB = "Нет";
        vm.Channels.Single(row => row.TopicId == NewTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.SelectedChannel?.TopicId == NewTopic, "second ballot channel opened");
        Assert.Equal("", vm.BallotQuestion);
        vm.BallotQuestion = "Вопрос Б";
        vm.BallotOptionA = "Рано";
        vm.BallotOptionB = "Поздно";
        vm.Channels.Single(row => row.TopicId == BallotTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.SelectedChannel?.TopicId == BallotTopic, "first ballot channel reopened");
        Assert.Equal("Вопрос А", vm.BallotQuestion);
        vm.Channels.Single(row => row.TopicId == NewTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.SelectedChannel?.TopicId == NewTopic, "second ballot channel reopened");
        Assert.Equal("Вопрос Б", vm.BallotQuestion);

        await vm.ProposeBallotCommand.ExecuteAsync(null);
        Assert.Equal(NewTopic, fixture.CreatedBallotTopic);
        Assert.Equal("", vm.BallotQuestion);
        vm.Channels.Single(row => row.TopicId == BallotTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.SelectedChannel?.TopicId == BallotTopic, "first ballot channel reopened again");
        Assert.Equal("Вопрос А", vm.BallotQuestion);
    }

    [AvaloniaFact]
    public async Task Closing_a_ballot_requires_a_second_explicit_action()
    {
        using var fixture = new Fixture(canManage: true, canCloseBallots: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        vm.Channels.Single(row => row.TopicId == BallotTopic).OpenCommand.Execute(null);
        await Waits.Until(() => vm.ShowBallots && vm.Ballots.Count == 1, "ballot channel opened");
        await vm.Ballots[0].CloseCommand.ExecuteAsync(null);
        Assert.True(vm.HasPendingCloseBallot);
        Assert.Equal(0, fixture.CloseCalls);
        vm.CancelCloseBallotCommand.Execute(null);
        Assert.False(vm.HasPendingCloseBallot);
        await vm.Ballots[0].CloseCommand.ExecuteAsync(null);
        await vm.ConfirmCloseBallotCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.CloseCalls);
        Assert.False(vm.HasPendingCloseBallot);
    }

    [AvaloniaFact]
    public async Task Desk_poll_hides_trust_controls_after_headman_demotion()
    {
        using var fixture = new Fixture(canManage: true, headman: true);
        var vm = fixture.Vm;
        await vm.ActivateAsync();
        Assert.True(vm.IsHeadman);
        vm.Watch(true);
        fixture.DemoteHeadman();
        await Waits.Until(() => !vm.IsHeadman, "headman rights refreshed", 6500);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ProfileTestDirectory directory = new();
        private readonly AppServices services;
        private readonly AccountClientHandler handler = new();
        private readonly HttpClient http;
        private readonly CommunityHttpClient client;
        private bool canManage;
        private bool headman;
        private readonly bool deskAvailable;
        private readonly bool restricted;
        private readonly bool canCloseBallots;
        public readonly List<string> Requests = [];
        public readonly List<string> Methods = [];
        public Guid? SentTopic;
        public string? SentBody;
        public Guid? VotedOption;
        public string? CreatedKind;
        public string? RenamedKind;
        public (string Description, string Accent, bool Pinned, string WritePolicy) CreatedMetadata;
        public (string Description, string Accent, bool Pinned, string WritePolicy) RenamedMetadata;
        public int DeleteCalls;
        public int CloseCalls;
        public Guid? CreatedBallotTopic;
        public string[] CreatedOptions = [];
        public List<string> TrustActions { get; } = [];
        public bool FailBallotGet;
        public bool FailBallotCreate;
        private BallotBoardResponse? boardOverride;
        public TaskCompletionSource? SendStarted;
        public TaskCompletionSource<HttpResponseMessage>? SendRelease;
        private readonly List<GroupTopicResponse> topics;
        private readonly List<GroupRoleResponse> roles = [];
        private readonly List<GroupPowerResponse> powers = [];
        private readonly List<GroupGrantResponse> grants = [];
        public GroupViewModel Vm { get; }

        public Fixture(bool canManage, bool headman = false, bool restricted = false, bool canCloseBallots = false)
        {
            this.canManage = canManage;
            this.headman = headman;
            deskAvailable = headman;
            this.restricted = restricted;
            this.canCloseBallots = canCloseBallots;
            topics = [
                new(null, "Общий", "💬", null, null, null, 0, false, "chat"),
                new(ChatTopic, "Расписание", "📅", null, null, null, 0, canManage, "chat", 0, "", "default", false,
                    restricted ? "managers" : "all", !restricted),
                new(BallotTopic, "Голосования", "🗳", null, null, null, 0, canManage, "ballots", 1, "", "default", false,
                    restricted ? "managers" : "all", !restricted)
            ];
            services = AppServices.Create(directory.Root, () => false);
            services.AllowNetwork = false;
            http = new HttpClient(handler);
            client = new CommunityHttpClient(http, Root);
            services.UseCommunities(client, _ => Task.FromResult<string?>(Access));
            handler.Send = Respond;
            Vm = new GroupViewModel(services);
        }

        public void Dispose()
        {
            Vm.Detach();
            client.Dispose();
            http.Dispose();
            services.Dispose();
            directory.Dispose();
        }

        public void AddExternalChannel(bool pinned = false) => topics.Add(new(NewTopic, "Новости", "📌", null, null, null, 0, canManage,
            "chat", 0, "Важные обновления", "blue", pinned));

        public void SetUnread(Guid topicId, int unread)
        {
            var old = topics.Single(item => item.TopicId == topicId);
            topics[topics.IndexOf(old)] = new(old.TopicId, old.Title, old.Icon, old.LastBody, old.LastAuthor,
                old.LastAt, unread, old.CanDelete, old.Kind, old.ActiveBallots, old.Description, old.Accent,
                old.Pinned, old.WritePolicy, old.CanPost);
        }

        public void RevokeChannelAccess()
        {
            canManage = false;
            var old = topics.Single(item => item.TopicId == ChatTopic);
            topics[topics.IndexOf(old)] = new(ChatTopic, old.Title, old.Icon, old.LastBody, old.LastAuthor, old.LastAt,
                old.Unread, false, "chat", 0, old.Description, old.Accent, old.Pinned, "managers", false);
        }

        public void RemoveChannel(Guid topicId) => topics.RemoveAll(item => item.TopicId == topicId);

        public void AddExternalBallotChannel() => topics.Add(new(NewTopic, "Выбор времени", "🗳", null, null, null, 0,
            canManage, "ballots", 0));

        public void SetBoard(BallotBoardResponse board) => boardOverride = board;

        public void ChangeChatMetadata(string description, string accent, bool pinned, string policy)
        {
            var old = topics.Single(item => item.TopicId == ChatTopic);
            topics[topics.IndexOf(old)] = new(ChatTopic, old.Title, old.Icon, old.LastBody, old.LastAuthor, old.LastAt,
                old.Unread, old.CanDelete, old.Kind, old.ActiveBallots, description, accent, pinned, policy, old.CanPost);
        }

        public void DemoteHeadman() => headman = false;

        private async Task<HttpResponseMessage> Respond(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = request.RequestUri.Query;
            Methods.Add(request.Method.Method);
            Requests.Add(path + query);
            if (path.EndsWith("/communities", StringComparison.Ordinal)) return Payload(new[] { Membership });
            if (path.EndsWith("/home", StringComparison.Ordinal))
                return Payload(new GroupHomeResponse(CommunityId, "О3313", "О3313",
                    new(GroupChat, "group", CommunityId, "О3313", null, null, null, 0),
                    [new(UserId, "student", "Аня", headman ? "headman" : "member", true),
                     new(PromotedId, "boris", "Борис", "member", false)], []));
            if (path.EndsWith("/desk", StringComparison.Ordinal) && request.Method == HttpMethod.Get && deskAvailable)
                return Payload(Desk());
            if (path.EndsWith("/roles", StringComparison.Ordinal) && request.Method == HttpMethod.Post && headman)
            {
                TrustActions.Add("create");
                roles.Add(new(NewTopic, "Доверенный по каналам"));
                return Payload(Desk(), HttpStatusCode.Created);
            }
            if (path.EndsWith($"/roles/{NewTopic:D}/powers", StringComparison.Ordinal) && request.Method == HttpMethod.Post && headman)
            {
                TrustActions.Add("power");
                powers.Add(new(NewTopic, "channels"));
                return Payload(Desk());
            }
            if (path.EndsWith($"/roles/{NewTopic:D}/grants", StringComparison.Ordinal) && request.Method == HttpMethod.Post && headman)
            {
                TrustActions.Add("grant");
                grants.Add(new(NewTopic, PromotedId));
                return Payload(Desk());
            }
            if (path.EndsWith($"/roles/{NewTopic:D}/grants/{PromotedId:D}/delete", StringComparison.Ordinal) && request.Method == HttpMethod.Post && headman)
            {
                TrustActions.Add("revoke");
                grants.Clear();
                return Payload(Desk());
            }
            if (path.EndsWith("/topics", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
                return Payload(Topics());
            if (path.EndsWith("/topics", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                CreatedKind = json.RootElement.GetProperty("kind").GetString();
                CreatedMetadata = Metadata(json.RootElement);
                topics.Add(new(NewTopic, json.RootElement.GetProperty("title").GetString()!, json.RootElement.GetProperty("icon").GetString()!,
                    null, null, null, 0, true, CreatedKind!, 0, CreatedMetadata.Description, CreatedMetadata.Accent,
                    CreatedMetadata.Pinned, CreatedMetadata.WritePolicy));
                return Payload(Topics(), HttpStatusCode.Created);
            }
            if (path.EndsWith($"/topics/{NewTopic:D}", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                RenamedKind = json.RootElement.GetProperty("kind").GetString();
                RenamedMetadata = Metadata(json.RootElement);
                var before = topics.Single(item => item.TopicId == NewTopic);
                topics[topics.IndexOf(before)] = new(NewTopic, json.RootElement.GetProperty("title").GetString()!,
                    json.RootElement.GetProperty("icon").GetString()!, null, null, null, 0, true, before.Kind, 0,
                    RenamedMetadata.Description, RenamedMetadata.Accent, RenamedMetadata.Pinned, RenamedMetadata.WritePolicy);
                return Payload(Topics());
            }
            if (path.EndsWith($"/topics/{NewTopic:D}/delete", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                DeleteCalls++;
                topics.RemoveAll(item => item.TopicId == NewTopic);
                return Payload(Topics());
            }
            if (path.EndsWith("/messages", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
                return Payload(new ChatPageResponse([], false));
            if (path.EndsWith("/topic-messages", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var target = json.RootElement.GetProperty("topicId");
                SentTopic = target.ValueKind == JsonValueKind.Null ? null : target.GetGuid();
                SentBody = json.RootElement.GetProperty("body").GetString();
                if (SendRelease is not null)
                {
                    SendStarted?.TrySetResult();
                    return await SendRelease.Task.WaitAsync(ct);
                }
                return Payload(new ChatMessageResponse(PollId, GroupChat, UserId, "Аня", SentBody!, CommunityClientTestSupport.Now), HttpStatusCode.Created);
            }
            if (path.EndsWith("/ballots", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                if (FailBallotGet) return Problem(503, "unavailable");
                return Payload(Board());
            }
            if ((path.EndsWith("/ballots/headman", StringComparison.Ordinal) || path.EndsWith("/ballots/collective", StringComparison.Ordinal))
                && request.Method == HttpMethod.Post)
            {
                if (FailBallotCreate) return Problem(503, "unavailable");
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var target = json.RootElement.GetProperty("topicId");
                CreatedBallotTopic = target.ValueKind == JsonValueKind.Null ? null : target.GetGuid();
                CreatedOptions = json.RootElement.GetProperty("options").EnumerateArray().Select(option => option.GetString()!).ToArray();
                return Payload(Board(), HttpStatusCode.Created);
            }
            if (path.EndsWith("/votes", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                VotedOption = json.RootElement.GetProperty("optionId").GetGuid();
                return Payload(Board());
            }
            if (path.EndsWith("/close", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                CloseCalls++;
                return Payload(Board());
            }
            if (path.EndsWith("/read", StringComparison.Ordinal))
                return Payload(new ConversationResponse(GroupChat, "group", CommunityId, "О3313", null, null, null, 0));
            return Problem(404, "not_found");
        }

        private GroupTopicListResponse Topics() => new(topics.ToArray(), canManage);
        private static (string Description, string Accent, bool Pinned, string WritePolicy) Metadata(JsonElement json) =>
            (json.GetProperty("description").GetString()!, json.GetProperty("accent").GetString()!,
                json.GetProperty("pinned").GetBoolean(), json.GetProperty("writePolicy").GetString()!);
        private GroupDeskResponse Desk() => new(headman, roles.ToArray(), grants.ToArray(), [], powers.ToArray(),
            headman ? ["roles", "grants", "channels"] : []);

        private BallotBoardResponse Board() => boardOverride ?? new(false, true, canCloseBallots, 3, 2, [
            new(PollId, "Когда встречаемся?", "headman", "open", CommunityClientTestSupport.Now.AddDays(2), 0, 2, false,
                [new(OptionYes, "Завтра", 1, false), new(OptionNo, "В пятницу", 0, false)], "", "", BallotTopic)
        ]);
    }
}
