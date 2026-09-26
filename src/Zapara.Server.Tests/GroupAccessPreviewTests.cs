using Microsoft.Extensions.DependencyInjection;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
using Xunit;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class GroupAccessPreviewTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Browser_preview_uses_cookie_authorization_and_requires_csrf()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await db.Accounts.Migrations.EnsureAsync(Ct);
        await using var host = new WebAccountHost(db.Accounts, moduleSettings: new()
        {
            ["Communities:Enabled"] = "true", ["Communities:Schema"] = db.Schema
        });
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("access_preview_browser", WebAccountHost.Password));
        var session = await host.Login("access_preview_browser");
        var group = Guid.NewGuid(); await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, session.GetProperty("user").GetProperty("userId").GetGuid());
        host.Client.DefaultRequestHeaders.Add("X-Zapara-Group-Space", "1");
        var topics = await host.Send("POST", $"/communities/{group}/topics", 201, new GroupTopicRequest("Предпросмотр", "chat"));
        var topic = topics.GetProperty("topics").EnumerateArray().Single(t => t.GetProperty("topicId").ValueKind != System.Text.Json.JsonValueKind.Null);
        var request = new GroupTopicAccessRequest([new(null, "read", "deny")], topic.GetProperty("revision").GetInt64());
        var path = $"/communities/{group}/space/topics/{topic.GetProperty("topicId").GetGuid()}/access-preview";
        await host.Send("POST", path, 403, request, csrf: false);
        var preview = await host.Send("POST", path, 200, request);
        Assert.Equal(0, preview.GetProperty("affectedCount").GetInt32());
        Assert.Single(preview.GetProperty("beforeReaders").EnumerateArray());
        Assert.Single(preview.GetProperty("afterReaders").EnumerateArray());
    }

    [Fact]
    public async Task Preview_matches_save_counts_only_active_group_members_and_performs_no_writes()
    {
        await using var w = await World.Create();
        var role = Assert.Single((await w.Service.CreateRoleAsync(w.Head.AccessToken, w.Group, new("Читатель"), Ct)).Roles).RoleId;
        await w.Service.GrantRoleAsync(w.Head.AccessToken, w.Group, role, new(w.Member.User.UserId), Ct);
        var topic = await w.Topic("ballots");
        var request = new GroupTopicAccessRequest([new(null, "read", "deny"), new(role, "read", "allow"), new(null, "post", "deny")], topic.Revision);
        GroupTopicAccessPreviewResponse preview;
        await w.WriteBarrier(true);
        try
        {
            preview = CommunityJson.Parse<GroupTopicAccessPreviewResponse>(await w.Host.Send("POST", w.Path(topic.TopicId!.Value), 200, w.Head.AccessToken, CommunityJson.Serialize(request)));
        }
        finally { await w.WriteBarrier(false); }
        Assert.Equal(topic.Revision, preview.Revision);
        Assert.Equal(2, preview.AffectedCount);
        Assert.Equal(3, preview.Participants.Count);
        Assert.Equal(new[] { w.Head.User.UserId, w.Member.User.UserId, w.Curator.User.UserId }.Order(), preview.BeforeReaders.Order());
        Assert.Equal(new[] { w.Head.User.UserId, w.Member.User.UserId }.Order(), preview.AfterReaders.Order());
        var member = preview.Participants.Single(p => p.UserId == w.Member.User.UserId);
        Assert.Contains("vote", member.AfterPermissions); Assert.DoesNotContain("post", member.AfterPermissions);
        Assert.Contains(role.ToString(), member.Sources["read"]);
        Assert.Contains("Запрет", member.Sources["post"]);
        var curator = preview.Participants.Single(p => p.UserId == w.Curator.User.UserId);
        Assert.Contains("joins", curator.AfterPermissions); Assert.Contains("куратора", curator.Sources["joins"]);
        await w.Service.SetTopicAccessAsync(w.Head.AccessToken, w.Group, topic.TopicId!.Value, request, Ct);
        foreach (var actor in new[] { w.Head, w.Member, w.Curator })
        {
            var expected = preview.Participants.Single(p => p.UserId == actor.User.UserId).AfterPermissions;
            var actual = (await w.Service.SpaceAsync(actor.AccessToken, w.Group, Ct)).Topics.SingleOrDefault(t => t.TopicId == topic.TopicId);
            if (expected.Contains("read")) Assert.Equal(expected, actual!.Permissions);
            else Assert.Null(actual);
        }
    }

    [Fact]
    public async Task Preview_rejects_missing_access_foreign_topic_role_stale_revision_and_self_escalation()
    {
        await using var w = await World.Create();
        var topic = await w.Topic("chat"); var id = topic.TopicId!.Value;
        var noChanges = new GroupTopicAccessRequest([], topic.Revision);
        await w.Host.Problem("POST", w.Path(id), 403, "forbidden", w.Member.AccessToken, CommunityJson.Serialize(noChanges));
        await w.Host.Problem("POST", w.Path(id), 403, "forbidden", w.Foreign.AccessToken, CommunityJson.Serialize(noChanges));
        var foreignRole = Assert.Single((await w.Service.CreateRoleAsync(w.Foreign.AccessToken, w.ForeignGroup, new("Чужая роль"), Ct)).Roles).RoleId;
        await w.Host.Problem("POST", w.Path(id), 400, "invalid_request", w.Head.AccessToken,
            CommunityJson.Serialize(new GroupTopicAccessRequest([new(foreignRole, "read", "allow")], topic.Revision)));
        var foreignTopic = (await w.Service.CreateTopicAsync(w.Foreign.AccessToken, w.ForeignGroup, new("Чужая тема", "chat"), Ct, true)).Topics.Single(t => t.TopicId is not null);
        await w.Host.Problem("POST", w.Path(foreignTopic.TopicId!.Value), 404, "not_found", w.Head.AccessToken, CommunityJson.Serialize(noChanges));
        await w.Host.Problem("POST", w.Path(id), 409, "revision_conflict", w.Head.AccessToken, CommunityJson.Serialize(new GroupTopicAccessRequest([], topic.Revision + 1)));
        foreach (var rules in new GroupAccessRule[][]
        {
            [new(null, "read", "allow"), new(null, "read", "deny")],
            [new(null, "unknown-power", "allow")],
            [new(null, "read", "unknown-state")]
        })
        {
            var invalid = new GroupTopicAccessRequest(rules, topic.Revision);
            await w.Host.Problem("POST", w.Path(id), 400, "invalid_request", w.Head.AccessToken, CommunityJson.Serialize(invalid));
            Assert.Equal(400, (await Assert.ThrowsAsync<CommunityServiceException>(() => w.Service.SetTopicAccessAsync(w.Head.AccessToken, w.Group, id, invalid, Ct))).Status);
        }
        var role = Assert.Single((await w.Service.CreateRoleAsync(w.Head.AccessToken, w.Group, new("Доступ"), Ct)).Roles).RoleId;
        await w.Service.SetRolePowerAsync(w.Head.AccessToken, w.Group, role, new("access", true), Ct);
        await w.Service.GrantRoleAsync(w.Head.AccessToken, w.Group, role, new(w.Member.User.UserId), Ct);
        topic = (await w.Service.SpaceAsync(w.Head.AccessToken, w.Group, Ct)).Topics.Single(t => t.TopicId == id);
        await w.Service.SetTopicAccessAsync(w.Head.AccessToken, w.Group, id, new([new(null, "post", "deny")], topic.Revision), Ct);
        var escalation = new GroupTopicAccessRequest([], topic.Revision + 1);
        await w.Host.Problem("POST", w.Path(id), 403, "forbidden", w.Member.AccessToken, CommunityJson.Serialize(escalation));
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => w.Service.SetTopicAccessAsync(w.Member.AccessToken, w.Group, id, escalation, Ct))).Status);
        await w.Service.SetTopicAccessAsync(w.Head.AccessToken, w.Group, id, new([new(null, "read", "deny")], topic.Revision + 1), Ct);
        await w.Host.Problem("POST", w.Path(id), 404, "not_found", w.Member.AccessToken, CommunityJson.Serialize(new GroupTopicAccessRequest([], topic.Revision + 2)));
    }

    [Fact]
    public async Task Preview_sources_include_template_and_archive_limits_without_content()
    {
        await using var w = await World.Create();
        var topic = await w.Topic("forms");
        await w.Service.ArchiveTopicAsync(w.Head.AccessToken, w.Group, topic.TopicId!.Value, new(true, topic.Revision), Ct);
        var request = new GroupTopicAccessRequest([new(null, "formsRespond", "allow"), new(null, "media", "allow")], topic.Revision + 1);
        var preview = CommunityJson.Parse<GroupTopicAccessPreviewResponse>(await w.Host.Send("POST", w.Path(topic.TopicId.Value), 200, w.Head.AccessToken, CommunityJson.Serialize(request)));
        Assert.Equal(0, preview.AffectedCount);
        Assert.All(preview.Participants, p =>
        {
            Assert.DoesNotContain("formsRespond", p.AfterPermissions);
            Assert.Contains("архив", p.Sources["formsRespond"]);
        });
        var schedule = await w.Topic("schedule");
        var schedulePreview = CommunityJson.Parse<GroupTopicAccessPreviewResponse>(await w.Host.Send("POST", w.Path(schedule.TopicId!.Value), 200, w.Head.AccessToken,
            CommunityJson.Serialize(new GroupTopicAccessRequest([new(null, "post", "allow")], schedule.Revision))));
        Assert.All(schedulePreview.Participants, p => Assert.Contains("Тип канала", p.Sources["post"]));
    }

    [Fact]
    public void Curator_keeps_protected_join_authority_despite_channel_deny()
        => Assert.Contains("joins", GroupPermissionRules.Calculate(true, false, true, [], [], [new(null, "joins", "deny")], "chat", "chat", false, "all"));

    private sealed class World : IAsyncDisposable
    {
        public required CommunityPostgresFixture Db { get; init; }
        public required CommunityApiTestHost Host { get; init; }
        public required SessionResponse Head { get; init; }
        public required SessionResponse Member { get; init; }
        public required SessionResponse Curator { get; init; }
        public required SessionResponse Foreign { get; init; }
        public Guid Group { get; } = Guid.NewGuid();
        public Guid ForeignGroup { get; } = Guid.NewGuid();
        public CommunityService Service => Host.App.Services.GetRequiredService<CommunityService>();
        public string Path(Guid topicId) => $"/{Group}/space/topics/{topicId}/access-preview";
        public static async Task<World> Create()
        {
            var db = await CommunityPostgresFixture.CreateAsync(true); var host = await CommunityApiTestHost.StartAsync(db, clock: new AccountClock());
            var w = new World { Db = db, Host = host, Head = await Seed(host.Accounts, "preview.head"), Member = await Seed(host.Accounts, "preview.member"), Curator = await Seed(host.Accounts, "preview.curator"), Foreign = await Seed(host.Accounts, "preview.foreign") };
            await db.SeedCommunityAsync(w.Group); await db.SeedStaffAsync(w.Group, w.Head.User.UserId); await db.SeedMemberAsync(w.Group, w.Member.User.UserId); await db.SeedStaffAsync(w.Group, w.Curator.User.UserId, "curator");
            await db.SeedCommunityAsync(w.ForeignGroup); await db.SeedStaffAsync(w.ForeignGroup, w.Foreign.User.UserId);
            await db.SeedMemberAsync(w.Group, w.Foreign.User.UserId);
            await db.Accounts.ExecuteAsync($"UPDATE {db.QuotedSchema}.memberships SET status='revoked',revoked_at=now() WHERE community_id='{w.Group}' AND user_id='{w.Foreign.User.UserId}'");
            return w;
        }
        public async Task<GroupTopicResponse> Topic(string kind)
            => (await Service.CreateTopicAsync(Head.AccessToken, Group, new("Тема " + kind, "chat", kind), Ct, true)).Topics.Single(t => t.Kind == kind && t.TopicId is not null);
        public Task WriteBarrier(bool enabled) => Db.Accounts.ExecuteAsync($"""
            CREATE OR REPLACE FUNCTION {Db.Configuration.QuotedMessages}.reject_preview_write() RETURNS trigger LANGUAGE plpgsql AS $f$
            BEGIN RAISE EXCEPTION 'preview attempted a database write'; END $f$;
            DO $barrier$ DECLARE item record; BEGIN
                FOR item IN SELECT n.nspname,c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE c.relkind='r' AND n.nspname IN ('{Db.Configuration.MessagesSchema}','{Db.Schema}','{Db.Accounts.Schema}') LOOP
                    EXECUTE format('DROP TRIGGER IF EXISTS preview_write_barrier ON %I.%I',item.nspname,item.relname);
                    { (enabled ? $"EXECUTE format('CREATE TRIGGER preview_write_barrier BEFORE INSERT OR UPDATE OR DELETE ON %I.%I FOR EACH STATEMENT EXECUTE FUNCTION {Db.Configuration.QuotedMessages}.reject_preview_write()',item.nspname,item.relname);" : "") }
                END LOOP;
            END $barrier$;
            """);
        public async ValueTask DisposeAsync() { await Host.DisposeAsync(); await Db.DisposeAsync(); }
    }
}
