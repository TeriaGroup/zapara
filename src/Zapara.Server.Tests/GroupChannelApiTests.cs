using Xunit;
using Zapara.Contracts.Communities;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class GroupChannelApiTests
{
    private static byte[] Json<T>(T value) => CommunityJson.Serialize(value);

    [Fact]
    public async Task Headman_can_delegate_channel_management_to_a_trusted_group_role()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var headman = await Seed(host.Accounts, "channels.headman");
        var member = await Seed(host.Accounts, "channels.member");
        var group = Guid.NewGuid();
        await db.SeedCommunityAsync(group);
        await db.SeedStaffAsync(group, headman.User.UserId);
        await db.SeedMemberAsync(group, member.User.UserId);

        await host.Problem("POST", $"/{group}/topics?typed=1", 403, "forbidden", member.AccessToken,
            Json(new GroupTopicRequest("Математика", "📚", "chat")));
        var opened = CommunityJson.Parse<GroupTopicListResponse>(await host.Send("POST", $"/{group}/topics?typed=1", 201,
            headman.AccessToken, Json(new GroupTopicRequest("Математика", "📚", "chat"))));
        Assert.True(opened.CanManageChannels);
        Assert.Equal("chat", Assert.Single(opened.Topics, item => item.TopicId is not null).Kind);
        Assert.False((await host.Get<GroupTopicListResponse>($"/{group}/topics?typed=1", member.AccessToken)).CanManageChannels);

        var desk = CommunityJson.Parse<GroupDeskResponse>(await host.Send("POST", $"/{group}/roles", 201,
            headman.AccessToken, Json(new GroupRoleNameRequest("Доверенный по чату"))));
        var role = Assert.Single(desk.Roles).RoleId;
        var proposal = CommunityJson.Parse<BallotBoardResponse>(await host.Send("POST", $"/{group}/ballots/changes", 201,
            member.AccessToken, Json(new BallotChangeRequest("power", 2, role, Guid.Empty, "", "channels", true))));
        Assert.Contains("Управлять каналами", Assert.Single(proposal.Ballots).Question);
        await host.Send("POST", $"/{group}/roles/{role}/powers", 200, headman.AccessToken,
            Json(new GroupPowerRequest("channels", true)));
        await host.Send("POST", $"/{group}/roles/{role}/grants", 200, headman.AccessToken,
            Json(new GroupGrantRequest(member.User.UserId)));

        Assert.True((await host.Get<GroupTopicListResponse>($"/{group}/topics?typed=1", member.AccessToken)).CanManageChannels);
        var created = CommunityJson.Parse<GroupTopicListResponse>(await host.Send("POST", $"/{group}/topics?typed=1", 201,
            member.AccessToken, Json(new GroupTopicRequest("Опросы", "🗳️", "ballots"))));
        var poll = Assert.Single(created.Topics, item => item.Kind == "ballots");
        Assert.True(poll.CanDelete);
        await host.Send("POST", $"/{group}/topics/{poll.TopicId}/delete?typed=1", 200, member.AccessToken);

        await host.Send("POST", $"/{group}/roles/{role}/grants/{member.User.UserId}/delete", 200, headman.AccessToken);
        await host.Problem("POST", $"/{group}/topics?typed=1", 403, "forbidden", member.AccessToken,
            Json(new GroupTopicRequest("После отзыва", "📌", "chat")));
    }

    [Fact]
    public async Task Ballot_channels_only_accept_structured_votes_and_chat_channels_keep_their_own_media()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var headman = await Seed(host.Accounts, "channels.vote.headman");
        var member = await Seed(host.Accounts, "channels.vote.member");
        var group = Guid.NewGuid();
        await db.SeedCommunityAsync(group);
        await db.SeedStaffAsync(group, headman.User.UserId);
        await db.SeedMemberAsync(group, member.User.UserId);
        var conversation = (await host.Get<GroupHomeResponse>($"/{group}/home", headman.AccessToken)).GroupChat.ConversationId;

        var chats = CommunityJson.Parse<GroupTopicListResponse>(await host.Send("POST", $"/{group}/topics?typed=1", 201,
            headman.AccessToken, Json(new GroupTopicRequest("Домашка", "📝", "chat"))));
        var chat = Assert.Single(chats.Topics, item => item.TopicId is not null).TopicId!.Value;
        var polls = CommunityJson.Parse<GroupTopicListResponse>(await host.Send("POST", $"/{group}/topics?typed=1", 201,
            headman.AccessToken, Json(new GroupTopicRequest("Посещение", "🗳️", "ballots"))));
        var poll = Assert.Single(polls.Topics, item => item.Kind == "ballots").TopicId!.Value;
        Assert.DoesNotContain((await host.Get<GroupTopicListResponse>($"/{group}/topics", member.AccessToken)).Topics,
            item => item.Kind == "ballots");
        Assert.Contains((await host.Get<GroupTopicListResponse>($"/{group}/topics?typed=1", member.AccessToken)).Topics,
            item => item.TopicId == poll);

        var topical = CommunityJson.Parse<ChatMessageResponse>(await host.Send("POST", $"/conversations/{conversation}/topic-messages", 201, member.AccessToken,
            Json(new TopicMessageRequest("Кто записал задание?", chat))));
        Assert.Single((await host.Get<ChatPageResponse>($"/conversations/{conversation}/messages?topic={chat:D}", member.AccessToken)).Messages);
        await host.Problem("POST", $"/conversations/{conversation}/messages", 400, "invalid_request", member.AccessToken,
            Json(new SendMessageRequest("Ответ не в том канале", topical.MessageId)));
        await host.Problem("POST", $"/conversations/{conversation}/topic-messages", 400, "invalid_request", member.AccessToken,
            Json(new TopicMessageRequest("Текст вместо опроса", poll)));
        await host.Problem("GET", $"/conversations/{conversation}/messages?topic={poll:D}", 400, "invalid_request", member.AccessToken);
        await host.SendMedia($"/conversations/{conversation}/media", member.AccessToken, "image", "photo.png", [1, 2, 3],
            status: 400, topicId: poll.ToString("D"));
        await host.SendMedia($"/conversations/{conversation}/media", member.AccessToken, "image", "photo.png", [1, 2, 3],
            topicId: chat.ToString("D"));
        Assert.Equal(2, (await host.Get<ChatPageResponse>($"/conversations/{conversation}/messages?topic={chat:D}", member.AccessToken)).Messages.Count);

        await host.Problem("POST", $"/{group}/ballots/headman", 400, "invalid_request", headman.AccessToken,
            Json(new BallotDraftRequest("Не в разговорный канал", ["Да", "Нет"], 2, chat)));
        await host.Send("POST", $"/{group}/ballots/headman", 201, headman.AccessToken,
            Json(new BallotDraftRequest("Кто придёт?", ["Приду", "Не приду"], 2, poll)));
        await host.Send("POST", $"/{group}/ballots/headman", 201, headman.AccessToken,
            Json(new BallotDraftRequest("Когда встреча?", ["Сегодня", "Завтра"], 2)));
        var scoped = await host.Get<BallotBoardResponse>($"/{group}/ballots?topic={poll:D}", member.AccessToken);
        var ballot = Assert.Single(scoped.Ballots);
        Assert.Equal(poll, ballot.TopicId);
        Assert.Equal("Кто придёт?", ballot.Question);
        Assert.Equal(2, (await host.Get<BallotBoardResponse>($"/{group}/ballots", member.AccessToken)).Ballots.Count);
        await host.Send("POST", $"/{group}/ballots/{ballot.BallotId}/votes", 200, member.AccessToken,
            Json(new VoteRequest(ballot.Options[0].OptionId)));
        Assert.True((await host.Get<BallotBoardResponse>($"/{group}/ballots?topic={poll:D}", member.AccessToken))
            .Ballots.Single().Options[0].Chosen);
        var preview = (await host.Get<GroupTopicListResponse>($"/{group}/topics?typed=1", member.AccessToken))
            .Topics.Single(item => item.TopicId == poll);
        Assert.Equal("ballots", preview.Kind);
        Assert.Equal("Кто придёт?", preview.LastBody);
        Assert.Equal(1, preview.ActiveBallots);

        await host.Send("POST", $"/{group}/topics/{poll:D}/delete?typed=1", 200, headman.AccessToken);
        Assert.Null((await host.Get<BallotBoardResponse>($"/{group}/ballots", member.AccessToken))
            .Ballots.Single(item => item.BallotId == ballot.BallotId).TopicId);
    }
}
