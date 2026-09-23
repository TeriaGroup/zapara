using System.Net;
using Xunit;
using Zapara.Contracts.Communities;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class MessengerApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static byte[] Json<T>(T value) => CommunityJson.Serialize(value);

    [Fact]
    public async Task Group_home_opens_one_chat_and_keeps_directs_inside_the_community()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var curator = await Seed(host.Accounts, "msg.curator");
        var head = await Seed(host.Accounts, "msg.head");
        var member = await Seed(host.Accounts, "msg.member");
        var outsider = await Seed(host.Accounts, "msg.outsider");
        var communityId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await db.SeedCommunityAsync(communityId, "О3313");
        await db.SeedCatalogAsync(communityId, "O3313", "О3313");
        await db.SeedCommunityAsync(otherId, "А863С");
        await db.SeedStaffAsync(communityId, curator.User.UserId, "curator");
        await db.SeedStaffAsync(communityId, head.User.UserId, "headman");
        await db.SeedMemberAsync(communityId, member.User.UserId);
        await db.SeedMemberAsync(otherId, head.User.UserId);
        await db.SeedMemberAsync(otherId, member.User.UserId);

        await host.Problem("GET", $"/{communityId}/home", 401, "invalid_session");
        await host.Problem("GET", $"/{communityId}/home", 403, "forbidden", outsider.AccessToken);
        var home = await host.Get<GroupHomeResponse>($"/{communityId}/home", member.AccessToken);
        var again = await host.Get<GroupHomeResponse>($"/{communityId}/home", head.AccessToken);
        Assert.Equal(home.GroupChat.ConversationId, again.GroupChat.ConversationId);
        Assert.Equal("group", home.GroupChat.Kind);
        Assert.Equal("О3313", home.GroupName);
        Assert.Equal(new[] { "curator", "headman", "member" }, home.Classmates.Select(person => person.Role).ToArray());
        Assert.Equal(member.User.UserId, home.Classmates.Single(person => person.Self).UserId);
        Assert.Empty(home.Directs);

        var sent = CommunityJson.Parse<ChatMessageResponse>(await host.Send("POST",
            $"/conversations/{home.GroupChat.ConversationId}/messages", 201, member.AccessToken, Json(new SendMessageRequest("строка\nдва"))));
        Assert.Equal("строка\nдва", sent.Body);
        var page = await host.Get<ChatPageResponse>($"/conversations/{home.GroupChat.ConversationId}/messages", head.AccessToken);
        Assert.Equal(sent.MessageId, Assert.Single(page.Messages).MessageId);
        Assert.False(page.HasMore);
        var headHome = await host.Get<GroupHomeResponse>($"/{communityId}/home", head.AccessToken);
        Assert.Equal(1, headHome.GroupChat.Unread);
        Assert.Equal(0, (await host.Get<GroupHomeResponse>($"/{communityId}/home", member.AccessToken)).GroupChat.Unread);
        var read = CommunityJson.Parse<ConversationResponse>(await host.Send("POST",
            $"/conversations/{home.GroupChat.ConversationId}/read", 200, head.AccessToken));
        Assert.Equal(0, read.Unread);
        Assert.Equal(0, (await host.Get<GroupHomeResponse>($"/{communityId}/home", head.AccessToken)).GroupChat.Unread);

        await host.Problem("POST", "/direct", 400, "invalid_request", member.AccessToken, Json(new OpenDirectRequest(communityId, member.User.UserId)));
        await host.Problem("POST", "/direct", 404, "not_found", member.AccessToken, Json(new OpenDirectRequest(communityId, outsider.User.UserId)));
        var direct = CommunityJson.Parse<ConversationResponse>(await host.Send("POST", "/direct", 201, member.AccessToken,
            Json(new OpenDirectRequest(communityId, head.User.UserId))));
        var same = CommunityJson.Parse<ConversationResponse>(await host.Send("POST", "/direct", 201, head.AccessToken,
            Json(new OpenDirectRequest(communityId, member.User.UserId))));
        Assert.Equal(direct.ConversationId, same.ConversationId);
        var elsewhere = CommunityJson.Parse<ConversationResponse>(await host.Send("POST", "/direct", 201, member.AccessToken,
            Json(new OpenDirectRequest(otherId, head.User.UserId))));
        Assert.NotEqual(direct.ConversationId, elsewhere.ConversationId);
        await host.Send("POST", $"/conversations/{direct.ConversationId}/messages", 201, head.AccessToken, Json(new SendMessageRequest("лично")));
        var memberHome = await host.Get<GroupHomeResponse>($"/{communityId}/home", member.AccessToken);
        Assert.Equal(direct.ConversationId, Assert.Single(memberHome.Directs).ConversationId);
        Assert.Equal(1, memberHome.Directs[0].Unread);
        Assert.DoesNotContain(memberHome.Directs, chat => chat.ConversationId == elsewhere.ConversationId);
        await host.Problem("GET", $"/conversations/{direct.ConversationId}/messages", 403, "forbidden", outsider.AccessToken);
        await host.Problem("POST", $"/conversations/{home.GroupChat.ConversationId}/messages", 400, "invalid_request", member.AccessToken, Json(new SendMessageRequest("  ")));

        await db.Accounts.ExecuteAsync($"""
            UPDATE {db.QuotedSchema}.memberships SET status='revoked', revoked_at=TIMESTAMPTZ '2026-09-08 12:06:00+00'
            WHERE community_id='{communityId}' AND user_id='{member.User.UserId}'
            """);
        await host.Problem("GET", $"/{communityId}/home", 403, "forbidden", member.AccessToken);
        await host.Problem("GET", $"/conversations/{home.GroupChat.ConversationId}/messages", 403, "forbidden", member.AccessToken);
        var left = await host.Get<GroupHomeResponse>($"/{communityId}/home", head.AccessToken);
        Assert.DoesNotContain(left.Classmates, person => person.UserId == member.User.UserId);
        Assert.Empty(left.Directs);
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM \"{db.Configuration.MessagesSchema}\".conversations WHERE kind='group'"));
    }

    [Fact]
    public async Task Message_pages_walk_backward_and_forward_without_mixing_cursors()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var member = await Seed(host.Accounts, "msg.page");
        var peer = await Seed(host.Accounts, "msg.peer");
        var communityId = Guid.NewGuid();
        await db.SeedCommunityAsync(communityId);
        await db.SeedMemberAsync(communityId, member.User.UserId);
        await db.SeedMemberAsync(communityId, peer.User.UserId);
        var home = await host.Get<GroupHomeResponse>($"/{communityId}/home", member.AccessToken);
        var ids = new List<Guid>();
        for (var i = 1; i <= 51; i++)
        {
            var message = CommunityJson.Parse<ChatMessageResponse>(await host.Send("POST",
                $"/conversations/{home.GroupChat.ConversationId}/messages", 201, member.AccessToken, Json(new SendMessageRequest("м" + i))));
            ids.Add(message.MessageId);
        }
        var latest = await host.Get<ChatPageResponse>($"/conversations/{home.GroupChat.ConversationId}/messages", peer.AccessToken);
        Assert.True(latest.HasMore);
        Assert.Equal(50, latest.Messages.Count);
        Assert.Equal(ids[1], latest.Messages[0].MessageId);
        Assert.Equal(ids[50], latest.Messages[^1].MessageId);
        var older = await host.Get<ChatPageResponse>($"/conversations/{home.GroupChat.ConversationId}/messages?before={latest.Messages[0].MessageId:D}", peer.AccessToken);
        Assert.False(older.HasMore);
        Assert.Equal(ids[0], Assert.Single(older.Messages).MessageId);
        var newer = await host.Get<ChatPageResponse>($"/conversations/{home.GroupChat.ConversationId}/messages?after={ids[0]:D}", peer.AccessToken);
        Assert.Equal(50, newer.Messages.Count);
        Assert.Equal(ids[1], newer.Messages[0].MessageId);
        Assert.False(newer.HasMore);
        await host.Problem("GET", $"/conversations/{home.GroupChat.ConversationId}/messages?before={ids[0]:D}&after={ids[1]:D}", 400, "invalid_request", peer.AccessToken);
        await host.Problem("GET", $"/conversations/{home.GroupChat.ConversationId}/messages?before={Guid.NewGuid():D}", 400, "invalid_request", peer.AccessToken);
        using var anonymous = await host.Client.GetAsync($"/api/v1/communities/{communityId}/home", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Media_message_keeps_its_kind_and_bytes_and_cannot_be_edited()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var member = await Seed(host.Accounts, "msg.media");
        var outsider = await Seed(host.Accounts, "msg.media.out");
        var communityId = Guid.NewGuid();
        await db.SeedCommunityAsync(communityId, "О3313");
        await db.SeedCatalogAsync(communityId, "O3313", "О3313");
        await db.SeedMemberAsync(communityId, member.User.UserId);
        var home = await host.Get<GroupHomeResponse>($"/{communityId}/home", member.AccessToken);
        var chat = home.GroupChat.ConversationId;
        var photo = new byte[] { 1, 2, 3, 4 };
        var clip = new byte[] { 9, 8, 7 };
        var notes = "конспект"u8.ToArray();
        var image = CommunityJson.Parse<ChatMessageResponse>(await host.SendMedia($"/conversations/{chat}/media", member.AccessToken, "image", "снимок.png", photo));
        var video = CommunityJson.Parse<ChatMessageResponse>(await host.SendMedia($"/conversations/{chat}/media", member.AccessToken, "video", "ролик.mp4", clip));
        var file = CommunityJson.Parse<ChatMessageResponse>(await host.SendMedia($"/conversations/{chat}/media", member.AccessToken, "file", "notes.txt", notes));
        Assert.Equal("image", image.Kind);
        Assert.Equal("снимок.png", image.Body);
        Assert.Equal("image", await db.Accounts.ScalarAsync<string>($"SELECT kind FROM \"{db.Configuration.MessagesSchema}\".chat_messages WHERE message_id='{image.MessageId}'"));
        Assert.Equal("video", await db.Accounts.ScalarAsync<string>($"SELECT kind FROM \"{db.Configuration.MessagesSchema}\".chat_messages WHERE message_id='{video.MessageId}'"));
        Assert.Equal("file", await db.Accounts.ScalarAsync<string>($"SELECT kind FROM \"{db.Configuration.MessagesSchema}\".chat_messages WHERE message_id='{file.MessageId}'"));
        var labeled = CommunityJson.Parse<ChatMessageResponse>(await host.Send("POST",
            $"/conversations/{chat}/messages", 201, member.AccessToken, Json(new SendMessageRequest("кадр.png", null, "image"))));
        Assert.Equal("image", labeled.Kind);
        Assert.Equal("кадр.png", labeled.Body);
        Assert.Equal("video", video.Kind);
        Assert.Equal("file", file.Kind);
        Assert.Equal(photo, await host.Send("GET", $"/conversations/{chat}/messages/{image.MessageId}/media", 200, member.AccessToken));
        Assert.Equal(clip, await host.Send("GET", $"/conversations/{chat}/messages/{video.MessageId}/media", 200, member.AccessToken));
        Assert.Equal(notes, await host.Send("GET", $"/conversations/{chat}/messages/{file.MessageId}/media", 200, member.AccessToken));
        var page = await host.Get<ChatPageResponse>($"/conversations/{chat}/messages", member.AccessToken);
        Assert.Equal(new[] { "image", "video", "file", "image" }, page.Messages.Select(item => item.Kind).ToArray());
        await host.Problem("POST", $"/conversations/{chat}/messages/{image.MessageId}/edit", 400, "invalid_request", member.AccessToken, Json(new SendMessageRequest("подпись")));
        var reacted = CommunityJson.Parse<ChatMessageResponse>(await host.Send("POST", $"/conversations/{chat}/messages/{image.MessageId}/react", 200, member.AccessToken, Json(new ReactMessageRequest("like"))));
        Assert.Equal("image", reacted.Kind);
        Assert.False(reacted.Deleted);
        await host.Problem("GET", $"/conversations/{chat}/messages/{image.MessageId}/media", 403, "forbidden", outsider.AccessToken);
        var removed = CommunityJson.Parse<ChatMessageResponse>(await host.Send("POST", $"/conversations/{chat}/messages/{image.MessageId}/delete", 200, member.AccessToken));
        Assert.True(removed.Deleted);
        Assert.Equal("image", removed.Kind);
        await host.Problem("GET", $"/conversations/{chat}/messages/{image.MessageId}/media", 404, "not_found", member.AccessToken);
    }
}
