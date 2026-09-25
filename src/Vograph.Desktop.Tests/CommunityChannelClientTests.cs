using System.Net;
using System.Text.Json;
using Vograph.Core.Services.Communities;
using Xunit;
using Zapara.Contracts.Communities;
using static Vograph.Desktop.Tests.AccountClientTestSupport;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class CommunityChannelClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid TopicId = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid ConversationId = Guid.Parse("66666666-6666-4666-8666-666666666666");

    [Fact]
    public async Task Topic_routes_send_kind_and_scope_messages_to_the_selected_channel()
    {
        var calls = new Queue<(string Method, string Path, string? Kind)>([
            ("GET", $"/{CommunityId:D}/topics?typed=1", null),
            ("POST", $"/{CommunityId:D}/topics?typed=1", "chat"),
            ("POST", $"/{CommunityId:D}/topics/{TopicId:D}?typed=1", "chat"),
            ("POST", $"/conversations/{ConversationId:D}/topic-messages", null),
            ("GET", $"/conversations/{ConversationId:D}/messages?topic={TopicId:D}&before={PollId:D}", null),
            ("POST", $"/{CommunityId:D}/topics/{TopicId:D}/delete?typed=1", null)
        ]);
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        handler.Send = async (request, ct) =>
        {
            var next = calls.Dequeue();
            Assert.Equal(next.Method, request.Method.Method);
            Assert.Equal("https://example.invalid/root/api/v2/communities" + next.Path, request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer " + Access, request.Headers.Authorization?.ToString());
            if (next.Kind is not null)
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal(next.Kind, json.RootElement.GetProperty("kind").GetString());
                Assert.Equal("Расписание", json.RootElement.GetProperty("title").GetString());
            }
            if (next.Path.EndsWith("/topic-messages", StringComparison.Ordinal))
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal(TopicId.ToString("D"), json.RootElement.GetProperty("topicId").GetString());
            }
            return next.Path.EndsWith("/topic-messages", StringComparison.Ordinal)
                ? Payload(new ChatMessageResponse(PollId, ConversationId, UserId, "Аня", "Привет", CommunityClientTestSupport.Now), HttpStatusCode.Created)
                : next.Path.Contains("/messages?", StringComparison.Ordinal)
                    ? Payload(new ChatPageResponse([], false))
                    : TextPayload($"{{\"topics\":[{{\"topicId\":\"{TopicId:D}\",\"title\":\"Расписание\",\"icon\":\"📅\",\"kind\":\"chat\",\"lastBody\":null,\"lastAuthor\":null,\"lastAt\":null,\"unread\":0,\"canDelete\":true,\"activeBallots\":0}}],\"canManageChannels\":true}}",
                        next.Method == "POST" && next.Path.EndsWith("/topics?typed=1", StringComparison.Ordinal) ? HttpStatusCode.Created : HttpStatusCode.OK);
        };

        var topics = await client.TopicsAsync(Access, CommunityId, Ct);
        Assert.True(topics.CanManageChannels);
        Assert.Equal("chat", Assert.Single(topics.Topics).Kind);
        await client.CreateTopicAsync(Access, CommunityId, new GroupTopicRequest("Расписание", "📅", "chat"), Ct);
        await client.RenameTopicAsync(Access, CommunityId, TopicId, new GroupTopicRequest("Расписание", "📅", "chat"), Ct);
        await client.SendTopicMessageAsync(Access, ConversationId, new TopicMessageRequest("Привет", TopicId), Ct);
        await client.MessagesAsync(Access, ConversationId, before: PollId, ct: Ct, topic: TopicId.ToString("D"));
        await client.DeleteTopicAsync(Access, CommunityId, TopicId, Ct);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Ballot_routes_scope_feed_and_creation_but_return_global_board_after_actions()
    {
        var calls = new Queue<(string Method, string Path, string? Topic)>([
            ("GET", $"/{CommunityId:D}/ballots?topic={TopicId:D}", null),
            ("POST", $"/{CommunityId:D}/ballots/headman", TopicId.ToString("D")),
            ("POST", $"/{CommunityId:D}/ballots/collective", TopicId.ToString("D")),
            ("POST", $"/{CommunityId:D}/ballots/{PollId:D}/support", null),
            ("POST", $"/{CommunityId:D}/ballots/{PollId:D}/votes", null),
            ("POST", $"/{CommunityId:D}/ballots/{PollId:D}/close", null)
        ]);
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        handler.Send = async (request, ct) =>
        {
            var next = calls.Dequeue();
            Assert.Equal(next.Method, request.Method.Method);
            Assert.Equal("https://example.invalid/root/api/v2/communities" + next.Path, request.RequestUri!.AbsoluteUri);
            if (next.Topic is not null)
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal(next.Topic, json.RootElement.GetProperty("topicId").GetString());
            }
            return Payload(new BallotBoardResponse(true, true, true, 3, 2, []),
                next.Path.EndsWith("/headman", StringComparison.Ordinal) || next.Path.EndsWith("/collective", StringComparison.Ordinal)
                    ? HttpStatusCode.Created : HttpStatusCode.OK);
        };

        Assert.Empty((await client.BallotsAsync(Access, CommunityId, TopicId, Ct)).Ballots);
        var draft = new BallotDraftRequest("Придёте?", ["Да", "Нет"], 2, TopicId);
        await client.OpenHeadmanBallotAsync(Access, CommunityId, draft, Ct);
        await client.ProposeBallotAsync(Access, CommunityId, draft, Ct);
        await client.SupportBallotAsync(Access, CommunityId, PollId, Ct);
        await client.VoteBallotAsync(Access, CommunityId, PollId, new VoteRequest(OptionYes), Ct);
        await client.CloseBallotAsync(Access, CommunityId, PollId, Ct);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Media_header_targets_only_a_custom_chat_channel()
    {
        var seen = new List<string?>();
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        handler.Send = (request, _) =>
        {
            seen.Add(request.Headers.TryGetValues("X-Zapara-Topic", out var values) ? Assert.Single(values) : null);
            return Task.FromResult(Payload(new ChatMessageResponse(PollId, ConversationId, UserId, "Аня", "Фото", CommunityClientTestSupport.Now, "image"), HttpStatusCode.Created));
        };

        await client.SendMediaAsync(Access, ConversationId, "image", "photo.png", [1, 2, 3], ct: Ct, topicId: TopicId);
        await client.SendMediaAsync(Access, ConversationId, "image", "photo.png", [1, 2, 3], ct: Ct);
        Assert.Equal([TopicId.ToString("D"), null], seen);
    }

    [Fact]
    public async Task Desk_routes_create_a_role_enable_channel_power_and_manage_its_grants()
    {
        var calls = new Queue<(string Method, string Path, string? Field, string? Value)>([
            ("GET", $"/{CommunityId:D}/desk", null, null),
            ("POST", $"/{CommunityId:D}/roles", "name", "Доверенный по каналам"),
            ("POST", $"/{CommunityId:D}/roles/{TopicId:D}/powers", "power", "channels"),
            ("POST", $"/{CommunityId:D}/roles/{TopicId:D}/grants", "userId", UserId.ToString("D")),
            ("POST", $"/{CommunityId:D}/roles/{TopicId:D}/grants/{UserId:D}/delete", null, null)
        ]);
        var desk = new GroupDeskResponse(true, [new(TopicId, "Доверенный по каналам")], [], [],
            [new(TopicId, "channels")], ["roles", "grants", "channels"]);
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        handler.Send = async (request, ct) =>
        {
            var next = calls.Dequeue();
            Assert.Equal(next.Method, request.Method.Method);
            Assert.Equal("https://example.invalid/root/api/v2/communities" + next.Path, request.RequestUri!.AbsoluteUri);
            if (next.Field is not null)
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal(next.Value, json.RootElement.GetProperty(next.Field).ToString());
            }
            return Payload(desk, next.Path.EndsWith("/roles", StringComparison.Ordinal) ? HttpStatusCode.Created : HttpStatusCode.OK);
        };

        Assert.True((await client.DeskAsync(Access, CommunityId, Ct)).Headman);
        await client.CreateRoleAsync(Access, CommunityId, new GroupRoleNameRequest("Доверенный по каналам"), Ct);
        await client.SetRolePowerAsync(Access, CommunityId, TopicId, new GroupPowerRequest("channels", true), Ct);
        await client.GrantRoleAsync(Access, CommunityId, TopicId, new GroupGrantRequest(UserId), Ct);
        await client.RevokeRoleAsync(Access, CommunityId, TopicId, UserId, Ct);
        Assert.Empty(calls);
    }
}
