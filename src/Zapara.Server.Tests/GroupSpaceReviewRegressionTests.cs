using Microsoft.Extensions.DependencyInjection;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
using Xunit;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class GroupSpaceReviewRegressionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("announcements", "all", "chat", "all")]
    [InlineData("announcements", "managers", "chat", "all")]
    public async Task Metadata_changes_cannot_give_delegate_new_channel_actions(string template, string policy, string proposedTemplate, string proposedPolicy)
    {
        await using var w = await World.Create();
        var role = Assert.Single((await w.Service.CreateRoleAsync(w.Head.AccessToken, w.Group, new("Редактор"), Ct)).Roles).RoleId;
        foreach (var power in new[] { "channels", "access" }) await w.Service.SetRolePowerAsync(w.Head.AccessToken, w.Group, role, new(power, true), Ct);
        await w.Service.GrantRoleAsync(w.Head.AccessToken, w.Group, role, new(w.Member.User.UserId), Ct);
        var topic = await w.Topic(new("Новости", "chat", template: template, writePolicy: policy));
        var chat = (await w.Service.GroupHomeAsync(w.Head.AccessToken, w.Group, Ct)).GroupChat.ConversationId;
        await Denied(() => w.Service.SendTopicMessageAsync(w.Member.AccessToken, chat, new("До", topic.TopicId), Ct));
        await Denied(() => w.Service.RenameTopicAsync(w.Member.AccessToken, w.Group, topic.TopicId!.Value,
            new(topic.Title, topic.Icon, template: proposedTemplate, writePolicy: proposedPolicy, expectedRevision: topic.Revision), Ct, true));
        await Denied(() => w.Service.SendTopicMessageAsync(w.Member.AccessToken, chat, new("После", topic.TopicId), Ct));
    }

    [Fact]
    public async Task Scoped_ballot_allow_works_without_global_ballot_or_close_power()
    {
        await using var w = await World.Create();
        var topic = await w.Topic(new("Решения", "vote", "ballots"));
        await w.Service.SetTopicAccessAsync(w.Head.AccessToken, w.Group, topic.TopicId!.Value,
            new([new(null, "ballots", "allow"), new(null, "close", "allow")], topic.Revision), Ct);
        var board = await w.Service.BallotsAsync(w.Member.AccessToken, w.Group, Ct, topic.TopicId);
        Assert.True(board.CanOpen); Assert.True(board.CanClose);
        var ballot = Assert.Single((await w.Service.OpenHeadmanBallotAsync(w.Member.AccessToken, w.Group,
            new("Вопрос", ["Да", "Нет"], 2, topic.TopicId), Ct)).Ballots);
        await w.Service.CloseBallotAsync(w.Member.AccessToken, w.Group, ballot.BallotId, Ct);
        await Denied(() => w.Service.OpenHeadmanBallotAsync(w.Member.AccessToken, w.Group, new("Общий", ["Да", "Нет"], 2), Ct));
        var global = (await w.Service.OpenHeadmanBallotAsync(w.Head.AccessToken, w.Group, new("Общий", ["Да", "Нет"], 2), Ct)).Ballots.Single(b => b.TopicId is null);
        await Denied(() => w.Service.CloseBallotAsync(w.Member.AccessToken, w.Group, global.BallotId, Ct));
    }

    [Fact]
    public async Task Legacy_rename_preserves_category_position_but_modern_null_clears_category()
    {
        await using var w = await World.Create();
        var category = Assert.Single((await w.Service.SaveCategoryAsync(w.Head.AccessToken, w.Group, new(null, "Учёба", 0), Ct)).Categories);
        var topic = await w.Topic(new("Старое имя", "chat", categoryId: category.CategoryId, position: 7));
        await w.Host.Send("POST", $"/{w.Group}/topics/{topic.TopicId}?typed=1", 200, w.Head.AccessToken,
            CommunityJson.Serialize(new GroupTopicRequest("Новое имя", "chat")));
        var preserved = (await w.Service.SpaceAsync(w.Head.AccessToken, w.Group, Ct)).Topics.Single(t => t.TopicId == topic.TopicId);
        Assert.Equal(category.CategoryId, preserved.CategoryId); Assert.Equal(7, preserved.Position);
        w.Host.Client.DefaultRequestHeaders.Add("X-Zapara-Group-Space", "1");
        await w.Host.Send("POST", $"/{w.Group}/topics/{topic.TopicId}?typed=1", 200, w.Head.AccessToken,
            CommunityJson.Serialize(new GroupTopicRequest(preserved.Title, preserved.Icon, template: "chat", categoryId: null, position: 0, expectedRevision: preserved.Revision)));
        var modern = (await w.Service.SpaceAsync(w.Head.AccessToken, w.Group, Ct)).Topics.Single(t => t.TopicId == topic.TopicId);
        Assert.Null(modern.CategoryId); Assert.Equal(0, modern.Position);
    }

    [Fact]
    public async Task Archive_preserves_authors_text_and_file_until_restore()
    {
        await using var w = await World.Create();
        var topic = await w.Topic(new("История", "chat"));
        var chat = (await w.Service.GroupHomeAsync(w.Member.AccessToken, w.Group, Ct)).GroupChat.ConversationId;
        var text = await w.Service.SendTopicMessageAsync(w.Member.AccessToken, chat, new("Сохранить", topic.TopicId), Ct);
        var file = await w.Service.SendMediaAsync(w.Member.AccessToken, chat, "file", "test.txt", [1, 2, 3], null, ct: Ct, topicId: topic.TopicId);
        await w.Service.ArchiveTopicAsync(w.Head.AccessToken, w.Group, topic.TopicId!.Value, new(true, topic.Revision), Ct);
        await Denied(() => w.Service.DeleteMessageAsync(w.Member.AccessToken, chat, text.MessageId, Ct));
        await Denied(() => w.Service.DeleteMessageAsync(w.Member.AccessToken, chat, file.MessageId, Ct));
        Assert.Equal(new byte[] { 1, 2, 3 }, await w.Service.ReadMediaAsync(w.Member.AccessToken, chat, file.MessageId, Ct));
        Assert.Contains((await w.Service.ListMessagesAsync(w.Member.AccessToken, chat, null, null, topic.TopicId.ToString(), Ct)).Messages, m => m.MessageId == text.MessageId && !m.Deleted);
        await w.Service.ArchiveTopicAsync(w.Head.AccessToken, w.Group, topic.TopicId.Value, new(false, topic.Revision + 1), Ct);
        Assert.True((await w.Service.DeleteMessageAsync(w.Member.AccessToken, chat, text.MessageId, Ct)).Deleted);
        Assert.True((await w.Service.DeleteMessageAsync(w.Member.AccessToken, chat, file.MessageId, Ct)).Deleted);
    }

    [Theory]
    [InlineData("rename_role")]
    [InlineData("power")]
    public async Task Collective_role_mutations_invalidate_stale_settings(string kind)
    {
        await using var w = await World.Create();
        var role = Assert.Single((await w.Service.CreateRoleAsync(w.Head.AccessToken, w.Group, new("До голосования"), Ct)).Roles);
        var ballot = Assert.Single((await w.Service.ProposeChangeAsync(w.Head.AccessToken, w.Group,
            new(kind, 1, role.RoleId, Guid.Empty, kind == "rename_role" ? "После голосования" : "", kind == "power" ? "channels" : "", true), Ct)).Ballots);
        await w.Service.SupportBallotAsync(w.Member.AccessToken, w.Group, ballot.BallotId, Ct);
        var yes = ballot.Options[0].OptionId;
        await w.Service.VoteBallotAsync(w.Head.AccessToken, w.Group, ballot.BallotId, new(yes), Ct);
        await w.Service.VoteBallotAsync(w.Member.AccessToken, w.Group, ballot.BallotId, new(yes), Ct);
        // Expire only this synthetic ballot, preserving live sessions.
        await w.Db.Accounts.ExecuteAsync($"UPDATE {w.Db.Configuration.QuotedMessages}.ballots SET deadline_at=TIMESTAMPTZ '2026-09-08 11:59:00+00' WHERE ballot_id='{ballot.BallotId}'");
        await w.Service.BallotsAsync(w.Head.AccessToken, w.Group, Ct);
        var updated = Assert.Single((await w.Service.DeskAsync(w.Head.AccessToken, w.Group, Ct)).Roles);
        Assert.True(updated.Revision > role.Revision);
        var conflict = await Assert.ThrowsAsync<CommunityServiceException>(() => w.Service.SaveRoleSettingsAsync(w.Head.AccessToken, w.Group, role.RoleId, new(role.Name, "user", 1, role.Revision), Ct));
        Assert.Equal(409, conflict.Status);
    }

    [Fact]
    public async Task New_groups_get_starter_topics_once_while_existing_groups_keep_ids_and_titles()
    {
        await using var w = await World.Create();
        var legacy = await w.Topic(new("Существующая тема", "chat"));
        var old = await w.Service.SpaceAsync(w.Head.AccessToken, w.Group, Ct);
        Assert.Equal("Общий", old.Topics.Single(t => t.TopicId is null).Title);
        var fresh = Guid.NewGuid();
        await w.Db.SeedCommunityAsync(fresh, starterTopics: true);
        await w.Db.SeedStaffAsync(fresh, w.Head.User.UserId);
        var first = await w.Service.SpaceAsync(w.Head.AccessToken, fresh, Ct);
        Assert.Equal(new[] { "Важное", "Опросы", "Чатик" }, first.Topics.Select(t => t.Title).Order().ToArray());
        var second = await w.Service.SpaceAsync(w.Head.AccessToken, fresh, Ct);
        Assert.Equal(first.Topics.Select(t => t.TopicId), second.Topics.Select(t => t.TopicId));
        var important = first.Topics.Single(t => t.Title == "Важное");
        await w.Service.ArchiveTopicAsync(w.Head.AccessToken, fresh, important.TopicId!.Value, new(true, important.Revision), Ct);
        await MessengerSchema.EnsureAsync(w.Db.Accounts.DataSource, w.Db.Configuration, Ct);
        var restored = await w.Service.SpaceAsync(w.Head.AccessToken, w.Group, Ct);
        Assert.Contains(restored.Topics, t => t.TopicId == legacy.TopicId && t.Title == legacy.Title);
        Assert.Equal("Общий", restored.Topics.Single(t => t.TopicId is null).Title);
        Assert.Equal(2, (await w.Service.SpaceAsync(w.Head.AccessToken, fresh, Ct)).Topics.Count);
        Assert.Equal(important.TopicId, Assert.Single((await w.Service.ArchivedTopicsAsync(w.Head.AccessToken, fresh, Ct)).Topics).TopicId);
    }

    [Fact]
    public async Task Schema_install_records_existing_communities_only_once()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        var legacy = Guid.NewGuid();
        await db.SeedCommunityAsync(legacy);
        await db.Accounts.ExecuteAsync($"""
            INSERT INTO {db.Configuration.QuotedMessages}.group_topics(topic_id,community_id,title,icon,created_by,created_at)
            VALUES(gen_random_uuid(),'{legacy}','Старое обсуждение','chat',NULL,now());
            DROP TABLE {db.Configuration.QuotedMessages}.group_space_state;
            DROP TABLE {db.Configuration.QuotedMessages}.group_space_bootstrap;
            """);
        var emptyBeforeInstall = Guid.NewGuid();
        await db.SeedCommunityAsync(emptyBeforeInstall, starterTopics: true);
        await MessengerSchema.EnsureAsync(db.Accounts.DataSource, db.Configuration, Ct);
        Assert.False(await db.Accounts.ScalarAsync<bool>($"SELECT starter_set FROM {db.Configuration.QuotedMessages}.group_space_state WHERE community_id='{legacy}'"));
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.Configuration.QuotedMessages}.group_space_state WHERE community_id='{emptyBeforeInstall}'"));
        var fresh = Guid.NewGuid();
        await db.SeedCommunityAsync(fresh, starterTopics: true);
        await MessengerSchema.EnsureAsync(db.Accounts.DataSource, db.Configuration, Ct);
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.Configuration.QuotedMessages}.group_space_state WHERE community_id='{fresh}'"));
    }

    [Fact]
    public async Task First_messenger_install_initializes_a_precreated_empty_catalog_group()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync();
        await db.Migrations.EnsureAsync(Ct);
        var group = Guid.NewGuid(); await db.SeedCommunityAsync(group);
        await using var host = await CommunityApiTestHost.StartAsync(db, clock: new AccountClock());
        var head = await Seed(host.Accounts, "fresh.catalog.head"); await db.SeedStaffAsync(group, head.User.UserId);
        var service = host.App.Services.GetRequiredService<CommunityService>();
        var space = await service.SpaceAsync(head.AccessToken, group, Ct);
        Assert.Equal(new[] { "Важное", "Опросы", "Чатик" }, space.Topics.Select(t => t.Title).Order().ToArray());
    }

    private static async Task Denied(Func<Task> operation) => Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(operation)).Status);

    private sealed class World : IAsyncDisposable
    {
        public required CommunityPostgresFixture Db { get; init; }
        public required CommunityApiTestHost Host { get; init; }
        public required SessionResponse Head { get; init; }
        public required SessionResponse Member { get; init; }
        public Guid Group { get; } = Guid.NewGuid();
        public CommunityService Service => Host.App.Services.GetRequiredService<CommunityService>();
        public static async Task<World> Create()
        {
            var db = await CommunityPostgresFixture.CreateAsync(true);
            var host = await CommunityApiTestHost.StartAsync(db, clock: new AccountClock());
            var w = new World { Db = db, Host = host, Head = await Seed(host.Accounts, "review.head"), Member = await Seed(host.Accounts, "review.member") };
            await db.SeedCommunityAsync(w.Group); await db.SeedStaffAsync(w.Group, w.Head.User.UserId); await db.SeedMemberAsync(w.Group, w.Member.User.UserId);
            return w;
        }
        public async Task<GroupTopicResponse> Topic(GroupTopicRequest request)
            => (await Service.CreateTopicAsync(Head.AccessToken, Group, request, Ct, true)).Topics.Single(t => t.Title == request.Title);
        public async ValueTask DisposeAsync() { await Host.DisposeAsync(); await Db.DisposeAsync(); }
    }
}
