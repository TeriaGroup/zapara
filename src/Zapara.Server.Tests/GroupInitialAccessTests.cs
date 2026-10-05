using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
using Xunit;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class GroupInitialAccessTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Request_omits_null_initial_acl_and_roundtrips_explicit_rules()
    {
        using var legacy = JsonDocument.Parse(CommunityJson.Serialize(new GroupTopicRequest("Публичный", "chat")));
        Assert.False(legacy.RootElement.TryGetProperty("initialAccessRules", out _));
        var rules = new GroupAccessRule[] { new(null, "read", "deny"), new(Guid.NewGuid(), "read", "allow") };
        var bytes = CommunityJson.Serialize(new GroupTopicRequest("Приватный", "chat", initialAccessRules: rules));
        Assert.Equal(rules, CommunityJson.Parse<GroupTopicRequest>(bytes).InitialAccessRules);
        using var empty = JsonDocument.Parse(CommunityJson.Serialize(new GroupTopicRequest("Публичный", "chat", initialAccessRules: [])));
        Assert.Equal(JsonValueKind.Array, empty.RootElement.GetProperty("initialAccessRules").ValueKind);
    }

    [Fact]
    public async Task Legacy_creation_keeps_old_response_shape_and_default_access()
    {
        await using var w = await World.Create();
        w.Host.Client.DefaultRequestHeaders.Remove("X-Zapara-Group-Space");
        using var page = JsonDocument.Parse(await w.Host.Send("POST", $"/{w.Group}/topics?typed=1", 201, w.Creator.AccessToken,
            CommunityJson.Serialize(new GroupTopicRequest("Старый клиент", "chat"))));
        var topic = page.RootElement.GetProperty("topics").EnumerateArray().Single(t => t.GetProperty("topicId").ValueKind != JsonValueKind.Null);
        Assert.False(topic.TryGetProperty("permissions", out _));
        Assert.False(topic.TryGetProperty("revision", out _));
        Assert.Contains((await w.Topics(w.Outside)).Topics, t => t.Title == "Старый клиент");
        Assert.Equal(0L, (await w.Counts()).Rules);
    }

    [Fact]
    public async Task No_public_topic_is_committed_while_initial_acl_insert_is_pending()
    {
        await using var w = await World.Create();
        var key = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
        await w.Db.Accounts.ExecuteAsync($"""
            CREATE FUNCTION {w.Db.Configuration.QuotedMessages}.pause_initial_acl() RETURNS trigger LANGUAGE plpgsql AS $f$
            BEGIN PERFORM pg_advisory_xact_lock({key}); RETURN NEW; END $f$;
            CREATE TRIGGER initial_acl_pause BEFORE INSERT ON {w.Db.Configuration.QuotedMessages}.group_topic_access
            FOR EACH ROW EXECUTE FUNCTION {w.Db.Configuration.QuotedMessages}.pause_initial_acl();
            """);
        await using var blocker = w.Db.Accounts.DataSource.CreateConnection();
        await blocker.OpenAsync(Ct);
        await using (var command = new Npgsql.NpgsqlCommand($"SELECT pg_advisory_lock({key})", blocker)) await command.ExecuteNonQueryAsync(Ct);
        var creating = w.Create(w.Head, new("Атомарная приватная", "lock", initialAccessRules: w.PrivateRules));
        var unlocked = false;
        try
        {
            var waiting = false;
            for (var i = 0; i < 100 && !creating.IsCompleted; i++)
            {
                waiting = await w.Db.Accounts.ScalarAsync<bool>($"SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE {blocker.ProcessID}=ANY(pg_blocking_pids(pid)) AND query LIKE '%group_topic_access%')");
                if (waiting) break;
                await Task.Delay(25, Ct);
            }
            Assert.True(waiting, "Create must be paused after topic insertion, at the initial ACL insert.");
            Assert.Equal(0L, await w.Db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {w.Db.Configuration.QuotedMessages}.group_topics WHERE community_id='{w.Group}' AND title='Атомарная приватная'"));
            await using (var command = new Npgsql.NpgsqlCommand($"SELECT pg_advisory_unlock({key})", blocker)) await command.ExecuteNonQueryAsync(Ct);
            unlocked = true;
            var topic = (await creating).Topics.Single(t => t.Title == "Атомарная приватная");
            Assert.DoesNotContain((await w.Topics(w.Outside)).Topics, t => t.TopicId == topic.TopicId);
            Assert.Contains((await w.Topics(w.Reader)).Topics, t => t.TopicId == topic.TopicId);
        }
        finally
        {
            if (!unlocked)
            {
                await using var command = new Npgsql.NpgsqlCommand($"SELECT pg_advisory_unlock({key})", blocker);
                await command.ExecuteNonQueryAsync(Ct);
            }
            try { await creating; } catch { /* Observe completion before the fixture is disposed. */ }
            await w.Db.Accounts.ExecuteAsync($"DROP TRIGGER initial_acl_pause ON {w.Db.Configuration.QuotedMessages}.group_topic_access; DROP FUNCTION {w.Db.Configuration.QuotedMessages}.pause_initial_acl();");
        }
    }

    [Fact]
    public async Task Native_private_creation_is_hidden_immediately_and_allowed_role_can_read_and_post()
    {
        await using var w = await World.Create();
        var request = new GroupTopicRequest("Секретная тема", "lock", initialAccessRules: w.PrivateRules);
        var topic = (await w.Create(w.Head, request)).Topics.Single(t => t.Title == request.Title);
        Assert.Equal(topic.Revision, (await w.Service.TopicAccessAsync(w.Head.AccessToken, w.Group, topic.TopicId!.Value, Ct)).Revision);
        Assert.DoesNotContain((await w.Topics(w.Outside)).Topics, t => t.TopicId == topic.TopicId);
        Assert.Contains((await w.Topics(w.Reader)).Topics, t => t.TopicId == topic.TopicId && t.CanPost);
        var chat = (await w.Service.GroupHomeAsync(w.Head.AccessToken, w.Group, Ct)).GroupChat.ConversationId;
        await w.Host.Send("POST", $"/conversations/{chat}/topic-messages", 201, w.Reader.AccessToken, CommunityJson.Serialize(new TopicMessageRequest("Скрытое сообщение", topic.TopicId)));
        await w.Host.Problem("POST", $"/conversations/{chat}/topic-messages", 404, "not_found", w.Outside.AccessToken, CommunityJson.Serialize(new TopicMessageRequest("Попытка", topic.TopicId)));
        Assert.Empty((await w.Service.ListMessagesAsync(w.Outside.AccessToken, chat, null, null, ct: Ct)).Messages);
        var outsiderHome = await w.Service.GroupHomeAsync(w.Outside.AccessToken, w.Group, Ct);
        Assert.Null(outsiderHome.GroupChat.LastBody); Assert.Equal(0, outsiderHome.GroupChat.Unread);
    }

    [Fact]
    public async Task Channels_only_defaults_work_but_custom_acl_is_forbidden_without_access()
    {
        await using var w = await World.Create();
        foreach (var rules in new IReadOnlyList<GroupAccessRule>?[] { null, [], [new(null, "read", "inherit")] })
            await w.Create(w.Creator, new("Публичная " + Guid.NewGuid().ToString("N")[..6], "chat", initialAccessRules: rules));
        var before = await w.Counts();
        await w.Host.Problem("POST", $"/{w.Group}/topics?typed=1", 403, "forbidden", w.Creator.AccessToken,
            CommunityJson.Serialize(new GroupTopicRequest("Недостаточно прав", "lock", initialAccessRules: w.PrivateRules)));
        Assert.Equal(before, await w.Counts());
        await w.Host.Problem("POST", $"/{w.Group}/topics?typed=1", 403, "forbidden", w.Outside.AccessToken,
            CommunityJson.Serialize(new GroupTopicRequest("Участник без прав", "chat")));
        Assert.Equal(before, await w.Counts());
    }

    [Fact]
    public async Task Invalid_foreign_group_only_or_escalating_acl_rolls_back_topic_and_audit()
    {
        await using var w = await World.Create();
        await w.Service.SetRolePowerAsync(w.Head.AccessToken, w.Group, w.CreatorRole, new("access", true), Ct);
        var other = Guid.NewGuid(); await w.Db.SeedCommunityAsync(other); await w.Db.SeedStaffAsync(other, w.Head.User.UserId);
        var foreignRole = Assert.Single((await w.Service.CreateRoleAsync(w.Head.AccessToken, other, new("Чужая роль"), Ct)).Roles).RoleId;
        var cases = new[]
        {
            (Actor:w.Head, Request:new GroupTopicRequest("Чужая роль", "lock", initialAccessRules:[new(foreignRole,"read","allow")]), Status:400, Code:"invalid_request"),
            (Actor:w.Head, Request:new GroupTopicRequest("Групповое право", "lock", initialAccessRules:[new(null,"grants","allow")]), Status:400, Code:"invalid_request"),
            (Actor:w.Creator, Request:new GroupTopicRequest("Самоповышение", "chat",template:"announcements",initialAccessRules:[new(w.CreatorRole,"post","allow")]), Status:403, Code:"forbidden"),
            (Actor:w.Creator, Request:new GroupTopicRequest("Нет права анкеты", "list","forms",initialAccessRules:[new(w.ReaderRole,"forms","allow")]), Status:403, Code:"forbidden")
        };
        var before = await w.Counts();
        foreach (var item in cases)
        {
            await w.Host.Problem("POST", $"/{w.Group}/topics?typed=1", item.Status, item.Code, item.Actor.AccessToken, CommunityJson.Serialize(item.Request));
            Assert.Equal(before, await w.Counts());
        }
    }

    [Fact]
    public async Task Creator_may_lose_read_and_still_receive_success_with_a_filtered_list()
    {
        await using var w = await World.Create();
        await w.Service.SetRolePowerAsync(w.Head.AccessToken, w.Group, w.CreatorRole, new("access", true), Ct);
        var response = await w.Create(w.Creator, new("Только читателю", "lock", initialAccessRules: w.PrivateRules));
        Assert.DoesNotContain(response.Topics, t => t.Title == "Только читателю");
        Assert.Contains((await w.Topics(w.Reader)).Topics, t => t.Title == "Только читателю");
        Assert.Equal(1L, await w.Db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {w.Db.Configuration.QuotedMessages}.group_topics WHERE community_id='{w.Group}' AND title='Только читателю'"));
    }

    [Fact]
    public async Task Rename_rejects_nonnull_create_only_acl_and_oversized_requests_never_insert()
    {
        await using var w = await World.Create();
        var topic = (await w.Create(w.Head, new("Существующая", "chat"))).Topics.Single(t => t.TopicId is not null);
        var before = await w.Counts();
        await w.Host.Problem("POST", $"/{w.Group}/topics/{topic.TopicId}?typed=1", 400, "invalid_request", w.Head.AccessToken,
            CommunityJson.Serialize(new GroupTopicRequest("Не менять", "chat", initialAccessRules: [])));
        Assert.Equal(topic.Title, (await w.Topics(w.Head)).Topics.Single(t => t.TopicId == topic.TopicId).Title);
        var large = CommunityJson.Serialize(new GroupTopicRequest("Слишком большой", "chat", initialAccessRules: Enumerable.Repeat(new GroupAccessRule(Guid.NewGuid(), "read", "deny"), 1200).ToArray()));
        Assert.True(large.Length > CommunityValidation.RequestBytes);
        await w.Host.Problem("POST", $"/{w.Group}/topics?typed=1", 413, "payload_too_large", w.Head.AccessToken, large);
        Assert.Equal(before, await w.Counts());
    }

    [Fact]
    public async Task Browser_creation_accepts_initial_acl_and_rejects_invalid_acl_atomically()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true); await db.Accounts.Migrations.EnsureAsync(Ct);
        await using var host = new WebAccountHost(db.Accounts, moduleSettings: new() { ["Communities:Enabled"] = "true", ["Communities:Schema"] = db.Schema });
        await host.Bootstrap(); await host.Send("POST", "/auth/register", 201, new RegisterRequest("initial_acl_browser", WebAccountHost.Password));
        var session = await host.Login("initial_acl_browser"); var group = Guid.NewGuid(); await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, session.GetProperty("user").GetProperty("userId").GetGuid());
        host.Client.DefaultRequestHeaders.Add("X-Zapara-Group-Space", "1");
        var page = await host.Send("POST", $"/communities/{group}/topics", 201, new GroupTopicRequest("Опросы закрытые", "vote", "ballots", initialAccessRules: [new(null, "read", "deny")]));
        var topic = page.GetProperty("topics").EnumerateArray().Single(t => t.GetProperty("kind").GetString() == "ballots").GetProperty("topicId").GetGuid();
        var access = await host.Send("GET", $"/communities/{group}/space/topics/{topic}/access", 200);
        Assert.Single(access.GetProperty("rules").EnumerateArray());
        await host.Send("POST", $"/communities/{group}/topics", 400, new GroupTopicRequest("Нельзя создать", "chat", initialAccessRules: [new(null, "roles", "allow")]));
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.Configuration.QuotedMessages}.group_topics WHERE community_id='{group}'"));
    }

    private sealed class World : IAsyncDisposable
    {
        public required CommunityPostgresFixture Db { get; init; }
        public required CommunityApiTestHost Host { get; init; }
        public required SessionResponse Head { get; init; }
        public required SessionResponse Reader { get; init; }
        public required SessionResponse Outside { get; init; }
        public required SessionResponse Creator { get; init; }
        public Guid Group { get; } = Guid.NewGuid();
        public Guid ReaderRole { get; private set; }
        public Guid CreatorRole { get; private set; }
        public GroupAccessRule[] PrivateRules => [new(null, "read", "deny"), new(ReaderRole, "read", "allow")];
        public CommunityService Service => Host.App.Services.GetRequiredService<CommunityService>();
        public static async Task<World> Create()
        {
            var db = await CommunityPostgresFixture.CreateAsync(true); var host = await CommunityApiTestHost.StartAsync(db, clock: new AccountClock());
            host.Client.DefaultRequestHeaders.Add("X-Zapara-Group-Space", "1");
            var w = new World { Db = db, Host = host, Head = await Seed(host.Accounts, "initial.head"), Reader = await Seed(host.Accounts, "initial.reader"), Outside = await Seed(host.Accounts, "initial.outside"), Creator = await Seed(host.Accounts, "initial.creator") };
            await db.SeedCommunityAsync(w.Group); await db.SeedStaffAsync(w.Group, w.Head.User.UserId);
            foreach (var actor in new[] { w.Reader, w.Outside, w.Creator }) await db.SeedMemberAsync(w.Group, actor.User.UserId);
            w.ReaderRole = Assert.Single((await w.Service.CreateRoleAsync(w.Head.AccessToken, w.Group, new("Читатель"), Ct)).Roles).RoleId;
            w.CreatorRole = (await w.Service.CreateRoleAsync(w.Head.AccessToken, w.Group, new("Создатель"), Ct)).Roles.Single(r => r.RoleId != w.ReaderRole).RoleId;
            await w.Service.SetRolePowerAsync(w.Head.AccessToken, w.Group, w.CreatorRole, new("channels", true), Ct);
            await w.Service.GrantRoleAsync(w.Head.AccessToken, w.Group, w.ReaderRole, new(w.Reader.User.UserId), Ct);
            await w.Service.GrantRoleAsync(w.Head.AccessToken, w.Group, w.CreatorRole, new(w.Creator.User.UserId), Ct);
            return w;
        }
        public async Task<GroupTopicListResponse> Create(SessionResponse actor, GroupTopicRequest request)
            => CommunityJson.Parse<GroupTopicListResponse>(await Host.Send("POST", $"/{Group}/topics?typed=1", 201, actor.AccessToken, CommunityJson.Serialize(request)));
        public Task<GroupTopicListResponse> Topics(SessionResponse actor) => Host.Get<GroupTopicListResponse>($"/{Group}/topics?typed=1", actor.AccessToken);
        public async Task<(long Topics, long Rules, long Events)> Counts()
            => (await Db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {Db.Configuration.QuotedMessages}.group_topics WHERE community_id='{Group}'"),
                await Db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {Db.Configuration.QuotedMessages}.group_topic_access a JOIN {Db.Configuration.QuotedMessages}.group_topics t ON t.topic_id=a.topic_id WHERE t.community_id='{Group}'"),
                await Db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {Db.Configuration.QuotedMessages}.group_management_audit WHERE community_id='{Group}' AND action='topic.created'"));
        public async ValueTask DisposeAsync() { await Host.DisposeAsync(); await Db.DisposeAsync(); }
    }
}
