using Xunit;
using Zapara.Contracts.Communities;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class GroupChannelEditPolicyTests
{
    [Fact]
    public async Task Former_manager_cannot_edit_a_message_in_a_manager_only_channel()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var author = await Seed(host.Accounts, "channel.edit.author");
        var group = Guid.NewGuid();
        await db.SeedCommunityAsync(group);
        await db.SeedStaffAsync(group, author.User.UserId);
        var conversation = (await host.Get<GroupHomeResponse>($"/{group}/home", author.AccessToken)).GroupChat.ConversationId;
        var channels = CommunityJson.Parse<GroupTopicListResponse>(await host.Send("POST", $"/{group}/topics?typed=1", 201,
            author.AccessToken, CommunityJson.Serialize(new GroupTopicRequest("Новости", "📌", "chat", "", "default", false, "managers"))));
        var topic = Assert.Single(channels.Topics, item => item.TopicId is not null).TopicId!.Value;
        var posted = CommunityJson.Parse<ChatMessageResponse>(await host.Send("POST", $"/conversations/{conversation}/topic-messages", 201,
            author.AccessToken, CommunityJson.Serialize(new TopicMessageRequest("Первый текст", topic))));

        await db.RevokeStaffAsync(group, author.User.UserId);

        await host.Problem("POST", $"/conversations/{conversation}/messages/{posted.MessageId}/edit", 403, "forbidden",
            author.AccessToken, CommunityJson.Serialize(new SendMessageRequest("Незаметная правка")));
    }
}
