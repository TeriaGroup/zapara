using Microsoft.Extensions.DependencyInjection;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
using Xunit;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class GroupTopicActionPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string[] TopicActions = ["channels", "access", "pin", "moderate"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Returned_topic_actions_match_allow_without_global_power_and_deny_despite_global_power(bool global)
    {
        await using var w = await World.Create(global);
        var rules = TopicActions.Select(p => new GroupAccessRule(w.Role, p, global ? "deny" : "allow")).ToArray();
        var topic = await w.Current();
        await w.Service.SetTopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, new(rules, topic.Revision), Ct);
        var view = await w.Service.SpaceAsync(w.Member.AccessToken, w.Group, Ct);
        var current = view.Topics.Single(t => t.TopicId == w.Topic);
        Assert.Equal(global, view.Capabilities is not null && view.Desk.Mine.Contains("channels"));
        Assert.Equal(!global, current.CanDelete);
        foreach (var action in TopicActions) Assert.Equal(!global, current.Permissions.Contains(action));
        var chat = (await w.Service.GroupHomeAsync(w.Head.AccessToken, w.Group, Ct)).GroupChat.ConversationId;
        var message = await w.Service.SendTopicMessageAsync(w.Head.AccessToken, chat, new("Сообщение автора", w.Topic), Ct);
        if (global)
        {
            await Denied(() => w.Service.TopicAccessAsync(w.Member.AccessToken, w.Group, w.Topic, Ct));
            await Denied(() => w.Service.TopicAccessPreviewAsync(w.Member.AccessToken, w.Group, w.Topic, new(rules, current.Revision), Ct));
            await Denied(() => w.Service.SetTopicAccessAsync(w.Member.AccessToken, w.Group, w.Topic, new(rules, current.Revision), Ct));
            await Denied(() => w.Service.RenameTopicAsync(w.Member.AccessToken, w.Group, w.Topic, new("Другое имя", "chat", expectedRevision: current.Revision), Ct));
            await Denied(() => w.Service.RenameTopicAsync(w.Member.AccessToken, w.Group, w.Topic, new(current.Title, current.Icon, pinned: true, expectedRevision: current.Revision), Ct));
            await Denied(() => w.Service.ArchiveTopicAsync(w.Member.AccessToken, w.Group, w.Topic, new(true, current.Revision), Ct));
            await Assert.ThrowsAsync<CommunityServiceException>(() => w.Service.DeleteMessageAsync(w.Member.AccessToken, chat, message.MessageId, Ct));
        }
        else
        {
            var access = await w.Service.TopicAccessAsync(w.Member.AccessToken, w.Group, w.Topic, Ct);
            var request = new GroupTopicAccessRequest(rules, access.Revision);
            Assert.Equal(0, (await w.Service.TopicAccessPreviewAsync(w.Member.AccessToken, w.Group, w.Topic, request, Ct)).AffectedCount);
            await w.Service.SetTopicAccessAsync(w.Member.AccessToken, w.Group, w.Topic, request, Ct);
            current = await w.Current();
            await w.Service.RenameTopicAsync(w.Member.AccessToken, w.Group, w.Topic, new("Другое имя", "chat", expectedRevision: current.Revision), Ct);
            current = await w.Current();
            await w.Service.RenameTopicAsync(w.Member.AccessToken, w.Group, w.Topic, new(current.Title, current.Icon, pinned: true, expectedRevision: current.Revision), Ct);
            Assert.True((await w.Service.DeleteMessageAsync(w.Member.AccessToken, chat, message.MessageId, Ct)).Deleted);
            current = await w.Current();
            await w.Service.ArchiveTopicAsync(w.Member.AccessToken, w.Group, w.Topic, new(true, current.Revision), Ct);
            var archived = Assert.Single((await w.Service.ArchivedTopicsAsync(w.Member.AccessToken, w.Group, Ct)).Topics);
            await w.Service.ArchiveTopicAsync(w.Member.AccessToken, w.Group, w.Topic, new(false, archived.Revision), Ct);
            await Denied(() => w.Service.CreateTopicAsync(w.Member.AccessToken, w.Group, new("Создать другой", "chat"), Ct));
            await Denied(() => w.Service.GroupAuditAsync(w.Member.AccessToken, w.Group, Ct));
        }
    }

    [Theory]
    [InlineData("access")]
    [InlineData("roles")]
    public async Task Audit_accepts_global_access_or_roles_but_filters_hidden_topic_events(string power)
    {
        await using var w = await World.Create(false);
        await w.Service.SetRolePowerAsync(w.Head.AccessToken, w.Group, w.Role, new(power, true), Ct);
        var access = await w.Service.TopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, Ct);
        await w.Service.SetTopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, new([new(w.Role, "read", "deny")], access.Revision), Ct);
        var audit = await w.Service.GroupAuditAsync(w.Member.AccessToken, w.Group, Ct);
        Assert.DoesNotContain(audit.Events, e => e.ObjectId == w.Topic);
        Assert.Contains(audit.Events, e => e.Action == "role.power");
    }

    [Fact]
    public async Task Group_only_powers_ignore_legacy_acl_and_new_overrides_are_rejected()
    {
        await using var w = await World.Create(false);
        await w.Service.SetRolePowerAsync(w.Head.AccessToken, w.Group, w.Role, new("joins", true), Ct);
        await w.Db.Accounts.ExecuteAsync($"""
            INSERT INTO {w.Db.Configuration.QuotedMessages}.group_topic_access(topic_id,role_id,power,state)
            VALUES('{w.Topic}','{w.Role}','joins','deny'),('{w.Topic}','{w.Role}','grants','allow');
            """);
        var view = (await w.Service.SpaceAsync(w.Member.AccessToken, w.Group, Ct)).Topics.Single(t => t.TopicId == w.Topic);
        Assert.Contains("joins", view.Permissions); Assert.DoesNotContain("grants", view.Permissions);
        var access = await w.Service.TopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, Ct);
        var preview = await w.Service.TopicAccessPreviewAsync(w.Head.AccessToken, w.Group, w.Topic, new(access.Rules, access.Revision), Ct);
        var member = preview.Participants.Single(p => p.UserId == w.Member.User.UserId);
        Assert.Contains("групповое полномочие", member.Sources["joins"]);
        Assert.Contains("групповое полномочие", member.Sources["grants"]);
        foreach (var power in GroupPermissionRules.GroupOnlyPowers)
        {
            var failure = await Assert.ThrowsAsync<CommunityServiceException>(() => w.Service.SetTopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, new([new(null, power, "allow")], access.Revision), Ct));
            Assert.Equal(400, failure.Status);
        }
        await w.Service.SetTopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, new(access.Rules, access.Revision), Ct);
        Assert.Empty((await w.Service.TopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, Ct)).Rules);
    }

    private static async Task Denied(Func<Task> operation) => Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(operation)).Status);
    private sealed class World : IAsyncDisposable
    {
        public required CommunityPostgresFixture Db { get; init; }
        public required CommunityApiTestHost Host { get; init; }
        public required SessionResponse Head { get; init; }
        public required SessionResponse Member { get; init; }
        public Guid Group { get; } = Guid.NewGuid();
        public Guid Role { get; private set; }
        public Guid Topic { get; private set; }
        public CommunityService Service => Host.App.Services.GetRequiredService<CommunityService>();
        public static async Task<World> Create(bool global)
        {
            var db = await CommunityPostgresFixture.CreateAsync(true); var host = await CommunityApiTestHost.StartAsync(db, clock: new AccountClock());
            var w = new World { Db = db, Host = host, Head = await Seed(host.Accounts, "actions.head"), Member = await Seed(host.Accounts, "actions.member") };
            await db.SeedCommunityAsync(w.Group); await db.SeedStaffAsync(w.Group, w.Head.User.UserId); await db.SeedMemberAsync(w.Group, w.Member.User.UserId);
            w.Role = Assert.Single((await w.Service.CreateRoleAsync(w.Head.AccessToken, w.Group, new("Управляющий"), Ct)).Roles).RoleId;
            if (global) foreach (var power in TopicActions) await w.Service.SetRolePowerAsync(w.Head.AccessToken, w.Group, w.Role, new(power, true), Ct);
            await w.Service.GrantRoleAsync(w.Head.AccessToken, w.Group, w.Role, new(w.Member.User.UserId), Ct);
            w.Topic = (await w.Service.CreateTopicAsync(w.Head.AccessToken, w.Group, new("Проверка", "chat"), Ct, true)).Topics.Single(t => t.TopicId is not null).TopicId!.Value;
            return w;
        }
        public async Task<GroupTopicResponse> Current() => (await Service.SpaceAsync(Head.AccessToken, Group, Ct)).Topics.Single(t => t.TopicId == Topic);
        public async ValueTask DisposeAsync() { await Host.DisposeAsync(); await Db.DisposeAsync(); }
    }
}
