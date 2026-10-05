using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

// These integration cases require the repository's explicitly configured test PostgreSQL.
[Trait("Category", "Postgres")]
public sealed class ReadReceiptDatabaseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task History_does_not_read_latest_and_incremental_read_stops_at_delivered_page()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var author = await Seed(host.Accounts, "read.author");
        var reader = await Seed(host.Accounts, "read.receiver");
        var group = Guid.NewGuid();
        await db.SeedCommunityAsync(group);
        await db.SeedMemberAsync(group, author.User.UserId);
        await db.SeedMemberAsync(group, reader.User.UserId);
        var service = host.App.Services.GetRequiredService<CommunityService>();
        var conversation = (await service.GroupHomeAsync(reader.AccessToken, group, Ct)).GroupChat.ConversationId;
        var sent = new List<ChatMessageResponse>();
        for (var index = 0; index < 102; index++)
            sent.Add(await service.SendMessageAsync(author.AccessToken, conversation, new SendMessageRequest("Сообщение " + index), Ct));

        var history = await service.ListMessagesAsync(reader.AccessToken, conversation, sent[70].MessageId, null, "general", Ct);
        Assert.Equal(50, history.Messages.Count);
        Assert.Equal(102, (await service.GroupHomeAsync(reader.AccessToken, group, Ct)).GroupChat.Unread);

        var page = await service.ListMessagesAsync(reader.AccessToken, conversation, null, sent[0].MessageId, "general", Ct);
        Assert.Equal(50, page.Messages.Count);
        Assert.True(page.HasMore);
        Assert.Equal(sent[50].MessageId, page.Messages[^1].MessageId);
        Assert.Equal(51, (await service.GroupHomeAsync(reader.AccessToken, group, Ct)).GroupChat.Unread);

        // A modern client's latest-page reconciliation fetch must not acknowledge any rows.
        await service.ListMessagesAsync(reader.AccessToken, conversation, null, null, "general", Ct, explicitReadCursor: true);
        Assert.Equal(51, (await service.GroupHomeAsync(reader.AccessToken, group, Ct)).GroupChat.Unread);
        var acknowledged = await service.MarkReadAsync(reader.AccessToken, conversation, Ct, sent[51].MessageId);
        Assert.Equal(50, acknowledged.Unread);
        var olderAcknowledgement = await service.MarkReadAsync(reader.AccessToken, conversation, Ct, sent[20].MessageId);
        Assert.Equal(50, olderAcknowledgement.Unread);
    }

    [Fact]
    public async Task Direct_target_is_monotonic_and_cannot_acknowledge_another_conversation()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var author = await Seed(host.Accounts, "direct.author");
        var reader = await Seed(host.Accounts, "direct.reader");
        var group = Guid.NewGuid();
        await db.SeedCommunityAsync(group);
        await db.SeedMemberAsync(group, author.User.UserId);
        await db.SeedMemberAsync(group, reader.User.UserId);
        var service = host.App.Services.GetRequiredService<CommunityService>();
        var direct = await service.OpenDirectAsync(author.AccessToken, new(group, reader.User.UserId), Ct);
        var first = await service.SendMessageAsync(author.AccessToken, direct.ConversationId, new("Первое"), Ct);
        var second = await service.SendMessageAsync(author.AccessToken, direct.ConversationId, new("Второе"), Ct);
        await service.SendMessageAsync(author.AccessToken, direct.ConversationId, new("Третье"), Ct);
        Assert.Equal(1, (await service.MarkReadAsync(reader.AccessToken, direct.ConversationId, Ct, second.MessageId)).Unread);
        Assert.Equal(1, (await service.MarkReadAsync(reader.AccessToken, direct.ConversationId, Ct, first.MessageId)).Unread);

        var groupChat = (await service.GroupHomeAsync(author.AccessToken, group, Ct)).GroupChat.ConversationId;
        var unrelated = await service.SendMessageAsync(author.AccessToken, groupChat, new("Другая беседа"), Ct);
        var error = await Assert.ThrowsAsync<CommunityServiceException>(() =>
            service.MarkReadAsync(reader.AccessToken, direct.ConversationId, Ct, unrelated.MessageId));
        Assert.Equal(400, error.Status);
        // Empty-body legacy calls remain an intentional mark-all operation.
        Assert.Equal(0, (await service.MarkReadAsync(reader.AccessToken, direct.ConversationId, Ct)).Unread);
    }
}
