using Microsoft.Extensions.DependencyInjection;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
using Xunit;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class GroupAccessSnapshotTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Save_rechecks_external_account_change_committed_while_topic_update_waits()
    {
        await using var w = await World.Create();
        var access = await w.Service.TopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, Ct);
        var proposal = new GroupTopicAccessRequest([new(null, "read", "deny"), new(w.Role, "read", "allow")], access.Revision);
        await w.Service.TopicAccessPreviewAsync(w.Head.AccessToken, w.Group, w.Topic, proposal, Ct);
        await using var connection = w.Db.Accounts.DataSource.CreateConnection();
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using (var command = new Npgsql.NpgsqlCommand($"""
            SELECT topic_id FROM {w.Db.Configuration.QuotedMessages}.group_topics WHERE topic_id='{w.Topic}' FOR UPDATE;
            UPDATE {w.Db.Accounts.QuotedSchema}.users SET status='disabled' WHERE user_id='{w.B.User.UserId}';
            """, connection, transaction)) await command.ExecuteNonQueryAsync(Ct);
        var save = w.Service.SetTopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, proposal, Ct);
        var committed = false;
        try
        {
            var blocked = false;
            for (var i = 0; i < 100 && !save.IsCompleted; i++)
            {
                blocked = await w.Db.Accounts.ScalarAsync<bool>($"SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE {connection.ProcessID}=ANY(pg_blocking_pids(pid)) AND query LIKE '%WITH snapshot AS%')");
                if (blocked) break;
                await Task.Delay(25, Ct);
            }
            Assert.True(blocked, "Save must have passed validation and be waiting at its conditional UPDATE.");
            await transaction.CommitAsync(Ct); committed = true;
            Assert.Equal(409, (await Assert.ThrowsAsync<CommunityServiceException>(() => save)).Status);
            Assert.Equal(access.Revision, await w.Db.Accounts.ScalarAsync<long>($"SELECT revision FROM {w.Db.Configuration.QuotedMessages}.group_topics WHERE topic_id='{w.Topic}'"));
        }
        finally
        {
            if (!committed) await transaction.RollbackAsync(Ct);
            try { await save; } catch (CommunityServiceException) { }
        }
        var fresh = await w.Service.TopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, Ct);
        var retry = new GroupTopicAccessRequest(proposal.Rules, fresh.Revision);
        await w.Service.TopicAccessPreviewAsync(w.Head.AccessToken, w.Group, w.Topic, retry, Ct);
        await w.Service.SetTopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, retry, Ct);
    }

    [Theory]
    [InlineData("grant")]
    [InlineData("revoke")]
    [InlineData("power")]
    [InlineData("rename")]
    [InlineData("join")]
    [InlineData("exclude")]
    [InlineData("official-role")]
    [InlineData("account-state")]
    [InlineData("account-removal")]
    [InlineData("collective-power")]
    [InlineData("collective-rename")]
    public async Task Changed_audience_or_authority_requires_fresh_metadata_and_preview(string change)
    {
        await using var w = await World.Create();
        var access = await w.Service.TopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, Ct);
        var proposal = new GroupTopicAccessRequest([new(null, "read", "deny"), new(w.Role, "read", "allow")], access.Revision);
        var first = await w.Service.TopicAccessPreviewAsync(w.Head.AccessToken, w.Group, w.Topic, proposal, Ct);
        Assert.Equal(2, first.AfterReaders.Count);
        await w.Change(change);
        var failure = await Assert.ThrowsAsync<CommunityServiceException>(() => w.Service.SetTopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, proposal, Ct));
        Assert.Equal(409, failure.Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<CommunityServiceException>(() => w.Service.TopicAccessPreviewAsync(w.Head.AccessToken, w.Group, w.Topic, proposal, Ct))).Status);
        var fresh = await w.Service.TopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, Ct);
        Assert.True(fresh.Revision > access.Revision);
        Assert.Equal(fresh.Revision, (await w.Service.TopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, Ct)).Revision);
        var next = new GroupTopicAccessRequest(proposal.Rules, fresh.Revision);
        var preview = await w.Service.TopicAccessPreviewAsync(w.Head.AccessToken, w.Group, w.Topic, next, Ct);
        if (change == "grant") Assert.Equal(3, preview.AfterReaders.Count);
        if (change == "revoke") Assert.Single(preview.AfterReaders);
        var saved = await w.Service.SetTopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, next, Ct);
        Assert.Contains(saved.Topics, t => t.TopicId == w.Topic);
    }

    [Fact]
    public async Task Snapshot_initializes_with_new_topic_and_unchanged_gets_do_not_revise_or_audit()
    {
        await using var w = await World.Create();
        var initial = await w.Service.TopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, Ct);
        var audit = (await w.Service.GroupAuditAsync(w.Head.AccessToken, w.Group, Ct)).Events.Count;
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(initial.Revision, (await w.Service.TopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, Ct)).Revision);
            Assert.Equal(initial.Revision, (await w.Service.SpaceAsync(w.Head.AccessToken, w.Group, Ct)).Topics.Single(t => t.TopicId == w.Topic).Revision);
        }
        Assert.Equal(audit, (await w.Service.GroupAuditAsync(w.Head.AccessToken, w.Group, Ct)).Events.Count);
        Assert.Equal(32, await w.Db.Accounts.ScalarAsync<int>($"SELECT octet_length(access_snapshot) FROM {w.Db.Configuration.QuotedMessages}.group_topics WHERE topic_id='{w.Topic}'"));
    }

    private sealed class World : IAsyncDisposable
    {
        public required CommunityPostgresFixture Db { get; init; }
        public required CommunityApiTestHost Host { get; init; }
        public required SessionResponse Head { get; init; }
        public required SessionResponse A { get; init; }
        public required SessionResponse B { get; init; }
        public required SessionResponse NewMember { get; init; }
        public Guid Group { get; } = Guid.NewGuid();
        public Guid Role { get; private set; }
        public Guid Topic { get; private set; }
        public CommunityService Service => Host.App.Services.GetRequiredService<CommunityService>();
        public static async Task<World> Create()
        {
            var db = await CommunityPostgresFixture.CreateAsync(true); var host = await CommunityApiTestHost.StartAsync(db, clock: new AccountClock());
            var w = new World { Db = db, Host = host, Head = await Seed(host.Accounts, "snapshot.head"), A = await Seed(host.Accounts, "snapshot.a"), B = await Seed(host.Accounts, "snapshot.b"), NewMember = await Seed(host.Accounts, "snapshot.new") };
            await db.SeedCommunityAsync(w.Group); await db.SeedStaffAsync(w.Group, w.Head.User.UserId); await db.SeedMemberAsync(w.Group, w.A.User.UserId); await db.SeedMemberAsync(w.Group, w.B.User.UserId);
            w.Role = Assert.Single((await w.Service.CreateRoleAsync(w.Head.AccessToken, w.Group, new("Читатели"), Ct)).Roles).RoleId;
            await w.Service.GrantRoleAsync(w.Head.AccessToken, w.Group, w.Role, new(w.A.User.UserId), Ct);
            var topic = (await w.Service.CreateTopicAsync(w.Head.AccessToken, w.Group, new("Скрытая тема", "chat"), Ct, true)).Topics.Single(t => t.TopicId is not null);
            Assert.Equal(1, topic.Revision);
            w.Topic = topic.TopicId!.Value;
            await w.Service.SetTopicAccessAsync(w.Head.AccessToken, w.Group, w.Topic, new([new(null, "read", "deny")], topic.Revision), Ct);
            return w;
        }
        public async Task Change(string change)
        {
            switch (change)
            {
                case "grant": await Service.GrantRoleAsync(Head.AccessToken, Group, Role, new(B.User.UserId), Ct); break;
                case "revoke": await Service.RevokeRoleAsync(Head.AccessToken, Group, Role, A.User.UserId, Ct); break;
                case "power": await Service.SetRolePowerAsync(Head.AccessToken, Group, Role, new("channels", true), Ct); break;
                case "rename": await Service.RenameRoleAsync(Head.AccessToken, Group, Role, new("Другие читатели"), Ct); break;
                case "join":
                    var join = await Service.RequestJoinAsync(NewMember.AccessToken, Group, Ct);
                    await Service.AcceptJoinAsync(Head.AccessToken, Group, join.RequestId, Ct); break;
                case "exclude": await Service.RemoveMemberAsync(Head.AccessToken, Group, B.User.UserId, Ct); break;
                case "official-role":
                    await Db.Accounts.ExecuteAsync($"UPDATE {Db.QuotedSchema}.memberships SET role='curator' WHERE community_id='{Group}' AND user_id='{B.User.UserId}'"); break;
                case "account-state":
                    await Db.Accounts.ExecuteAsync($"UPDATE {Db.Accounts.QuotedSchema}.users SET status='disabled' WHERE user_id='{B.User.UserId}'"); break;
                case "account-removal":
                    await Db.Accounts.ExecuteAsync($"DELETE FROM {Db.QuotedSchema}.memberships WHERE community_id='{Group}' AND user_id='{B.User.UserId}'"); break;
                case "collective-power": case "collective-rename":
                    var power = change == "collective-power";
                    var ballot = Assert.Single((await Service.ProposeChangeAsync(Head.AccessToken, Group,
                        new(power ? "power" : "rename_role", 1, Role, Guid.Empty, power ? "" : "После голосования", power ? "channels" : "", true), Ct)).Ballots);
                    foreach (var actor in new[] { A, B }) await Service.SupportBallotAsync(actor.AccessToken, Group, ballot.BallotId, Ct);
                    foreach (var actor in new[] { Head, A, B }) await Service.VoteBallotAsync(actor.AccessToken, Group, ballot.BallotId, new(ballot.Options[0].OptionId), Ct);
                    await Db.Accounts.ExecuteAsync($"UPDATE {Db.Configuration.QuotedMessages}.ballots SET deadline_at=TIMESTAMPTZ '2026-09-08 11:59:00+00' WHERE ballot_id='{ballot.BallotId}'");
                    await Service.BallotsAsync(Head.AccessToken, Group, Ct); break;
                default: throw new ArgumentOutOfRangeException(nameof(change));
            }
        }
        public async ValueTask DisposeAsync() { await Host.DisposeAsync(); await Db.DisposeAsync(); }
    }
}
