using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Zapara.Contracts.Communities;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class GroupChannelCustomizationApiTests
{
    private static byte[] Json<T>(T value) => CommunityJson.Serialize(value);

    [Fact]
    public async Task Pinned_manager_only_chat_keeps_its_style_and_refuses_member_posts()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var headman = await Seed(host.Accounts, "channel.style.head");
        var member = await Seed(host.Accounts, "channel.style.member");
        var group = Guid.NewGuid();
        await db.SeedCommunityAsync(group);
        await db.SeedStaffAsync(group, headman.User.UserId);
        await db.SeedMemberAsync(group, member.User.UserId);
        var conversation = (await host.Get<GroupHomeResponse>($"/{group}/home", headman.AccessToken)).GroupChat.ConversationId;

        var created = CommunityJson.Parse<GroupTopicListResponse>(await host.Send("POST", $"/{group}/topics?typed=1", 201,
            headman.AccessToken, Json(new GroupTopicRequest("Важное", "📌", "chat", "Только сообщения управляющих", "purple", true, "managers"))));
        var channel = Assert.Single(created.Topics, item => item.TopicId is not null);
        Assert.Equal("Только сообщения управляющих", channel.Description);
        Assert.Equal("purple", channel.Accent);
        Assert.True(channel.Pinned);
        Assert.Equal("managers", channel.WritePolicy);
        Assert.True(channel.CanPost);
        var memberView = (await host.Get<GroupTopicListResponse>($"/{group}/topics?typed=1", member.AccessToken))
            .Topics.Single(item => item.TopicId == channel.TopicId);
        Assert.False(memberView.CanPost);

        await host.Problem("POST", $"/conversations/{conversation}/topic-messages", 403, "forbidden", member.AccessToken,
            Json(new TopicMessageRequest("Попытка публикации", channel.TopicId)));
        await host.SendMedia($"/conversations/{conversation}/media", member.AccessToken, "image", "photo.png", [1, 2, 3],
            status: 403, topicId: channel.TopicId!.Value.ToString("D"));
        await host.Send("POST", $"/conversations/{conversation}/topic-messages", 201, headman.AccessToken,
            Json(new TopicMessageRequest("Объявление", channel.TopicId)));
        Assert.Single((await host.Get<ChatPageResponse>($"/conversations/{conversation}/messages?topic={channel.TopicId:D}", member.AccessToken)).Messages);

        var renamed = CommunityJson.Parse<GroupTopicListResponse>(await host.Send("POST", $"/{group}/topics/{channel.TopicId}?typed=1", 200,
            headman.AccessToken, Json(new GroupTopicRequest("Новости", "❗", "chat"))));
        var stillStyled = Assert.Single(renamed.Topics, item => item.TopicId == channel.TopicId);
        Assert.Equal("Только сообщения управляющих", stillStyled.Description);
        Assert.Equal("purple", stillStyled.Accent);
        Assert.True(stillStyled.Pinned);
        Assert.Equal("managers", stillStyled.WritePolicy);

        var reopened = CommunityJson.Parse<GroupTopicListResponse>(await host.Send("POST", $"/{group}/topics/{channel.TopicId}?typed=1", 200,
            headman.AccessToken, Json(new GroupTopicRequest("Новости", "❗", "chat", "Писать могут все", "green", false, "all"))));
        var current = Assert.Single(reopened.Topics, item => item.TopicId == channel.TopicId);
        Assert.Equal("Писать могут все", current.Description);
        Assert.Equal("green", current.Accent);
        Assert.False(current.Pinned);
        Assert.Equal("all", current.WritePolicy);
        Assert.True((await host.Get<GroupTopicListResponse>($"/{group}/topics?typed=1", member.AccessToken))
            .Topics.Single(item => item.TopicId == channel.TopicId).CanPost);
        await host.Send("POST", $"/conversations/{conversation}/topic-messages", 201, member.AccessToken,
            Json(new TopicMessageRequest("Ответ участника", channel.TopicId)));
    }

    [Fact]
    public async Task Manager_only_ballot_channel_allows_voting_but_limits_new_questions()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var headman = await Seed(host.Accounts, "channel.ballot.head");
        var member = await Seed(host.Accounts, "channel.ballot.member");
        var group = Guid.NewGuid();
        await db.SeedCommunityAsync(group);
        await db.SeedStaffAsync(group, headman.User.UserId);
        await db.SeedMemberAsync(group, member.User.UserId);
        var created = CommunityJson.Parse<GroupTopicListResponse>(await host.Send("POST", $"/{group}/topics?typed=1", 201,
            headman.AccessToken, Json(new GroupTopicRequest("Решения", "🗳️", "ballots", "Вопросы старосты", "blue", true, "managers"))));
        var channel = Assert.Single(created.Topics, item => item.Kind == "ballots");
        Assert.False((await host.Get<GroupTopicListResponse>($"/{group}/topics?typed=1", member.AccessToken))
            .Topics.Single(item => item.TopicId == channel.TopicId).CanPost);

        await host.Problem("POST", $"/{group}/ballots/collective", 403, "forbidden", member.AccessToken,
            Json(new BallotDraftRequest("Куда идём?", ["Парк", "Кафе"], 2, channel.TopicId)));
        await host.Send("POST", $"/{group}/ballots/headman", 201, headman.AccessToken,
            Json(new BallotDraftRequest("Куда идём?", ["Парк", "Кафе"], 2, channel.TopicId)));
        var board = await host.Get<BallotBoardResponse>($"/{group}/ballots?topic={channel.TopicId:D}", member.AccessToken);
        var ballot = Assert.Single(board.Ballots);
        await host.Send("POST", $"/{group}/ballots/{ballot.BallotId}/votes", 200, member.AccessToken,
            Json(new VoteRequest(ballot.Options[0].OptionId)));
        Assert.True((await host.Get<BallotBoardResponse>($"/{group}/ballots?topic={channel.TopicId:D}", member.AccessToken))
            .Ballots.Single().Options[0].Chosen);
    }
    [Fact]
    public async Task Deleting_a_chat_channel_removes_its_archived_text_and_media()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var headman = await Seed(host.Accounts, "channel.cleanup.head");
        var group = Guid.NewGuid();
        await db.SeedCommunityAsync(group);
        await db.SeedStaffAsync(group, headman.User.UserId);
        var conversation = (await host.Get<GroupHomeResponse>($"/{group}/home", headman.AccessToken)).GroupChat.ConversationId;
        var created = CommunityJson.Parse<GroupTopicListResponse>(await host.Send("POST", $"/{group}/topics?typed=1", 201,
            headman.AccessToken, Json(new GroupTopicRequest("Файлы", "📎", "chat"))));
        var topic = Assert.Single(created.Topics, item => item.TopicId is not null).TopicId!.Value;
        var text = CommunityJson.Parse<ChatMessageResponse>(await host.Send("POST", $"/conversations/{conversation}/topic-messages", 201,
            headman.AccessToken, Json(new TopicMessageRequest("Конспект", topic))));
        var photo = CommunityJson.Parse<ChatMessageResponse>(await host.SendMedia($"/conversations/{conversation}/media",
            headman.AccessToken, "image", "photo.png", [1, 2, 3], topicId: topic.ToString("D")));
        var archive = host.App.Services.GetRequiredService<IContentArchive>();
        Assert.NotNull(archive.Get(ContentNames.GroupMessage(text.MessageId)));
        Assert.NotNull(archive.Get(ContentNames.GroupFile(photo.MessageId)));

        await host.Send("POST", $"/{group}/topics/{topic:D}/delete?typed=1", 200, headman.AccessToken);

        Assert.Null(archive.Get(ContentNames.GroupMessage(text.MessageId)));
        Assert.Null(archive.Get(ContentNames.GroupMessage(photo.MessageId)));
        Assert.Null(archive.Get(ContentNames.GroupFile(photo.MessageId)));
    }

}
