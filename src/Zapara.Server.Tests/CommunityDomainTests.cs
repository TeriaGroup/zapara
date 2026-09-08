using Xunit;
using Zapara.Contracts.Communities;
using Zapara.Server.Accounts;
using Zapara.Server.Communities;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class CommunityDomainTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateTimeOffset Deadline => new(2026, 9, 8, 12, 10, 0, TimeSpan.Zero);

    [Fact]
    public async Task Catalog_group_selection_grants_no_role()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var session = await Seed(accounts, "catalog.user");
        var communityId = Guid.NewGuid();
        await db.SeedCommunityAsync(communityId);
        await db.SeedCatalogAsync(communityId);
        var service = new CommunityService(accounts, db.Configuration);
        var found = await service.ListAsync(session.AccessToken, "O3313", Ct);
        Assert.Single(found);
        Assert.Equal(communityId, found[0].CommunityId);
        Assert.Null(found[0].Role);
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.memberships"));
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.staff_assignments"));
        var denied = await Assert.ThrowsAsync<CommunityServiceException>(() =>
            service.PublishHomeworkAsync(session.AccessToken, communityId, new("ДЗ", "Текст задания", 0), Ct));
        Assert.Equal(403, denied.Status);
        Assert.Equal("forbidden", denied.Code);
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.shared_homework"));
    }

    [Fact]
    public async Task Revoked_staff_cannot_publish_after_revocation_commits()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var staff = await Seed(accounts, "staff.user");
        var communityId = Guid.NewGuid();
        await db.SeedCommunityAsync(communityId);
        await db.SeedStaffAsync(communityId, staff.User.UserId);
        var service = new CommunityService(accounts, db.Configuration);
        var published = await service.PublishHomeworkAsync(staff.AccessToken, communityId, new("ДЗ", "Текст", 0), Ct);
        Assert.Equal(1, published.Revision);
        await db.RevokeStaffAsync(communityId, staff.User.UserId);
        var denied = await Assert.ThrowsAsync<CommunityServiceException>(() =>
            service.PublishHomeworkAsync(staff.AccessToken, communityId, new("Ещё", "Нет", 0), Ct));
        Assert.Equal(403, denied.Status);
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.shared_homework"));
        var announcement = await Assert.ThrowsAsync<CommunityServiceException>(() =>
            service.PublishAnnouncementAsync(staff.AccessToken, communityId, new("Объявление", "Текст", 0), Ct));
        Assert.Equal(403, announcement.Status);
        var poll = await Assert.ThrowsAsync<CommunityServiceException>(() =>
            service.PublishPollAsync(staff.AccessToken, communityId, new("Вопрос?", Deadline, ["Да", "Нет"], 0), Ct));
        Assert.Equal(403, poll.Status);
    }

    [Fact]
    public async Task Completion_for_homework_A_does_not_complete_B()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var staff = await Seed(accounts, "head.user");
        var member = await Seed(accounts, "member.user");
        var communityId = Guid.NewGuid();
        await db.SeedCommunityAsync(communityId);
        await db.SeedStaffAsync(communityId, staff.User.UserId);
        await db.SeedMemberAsync(communityId, member.User.UserId);
        var service = new CommunityService(accounts, db.Configuration);
        var a = await service.PublishHomeworkAsync(staff.AccessToken, communityId, new("Задание А", "Описание А", 0), Ct);
        var b = await service.PublishHomeworkAsync(staff.AccessToken, communityId, new("Задание Б", "Описание Б", 0), Ct);
        var done = await service.UpsertCompletionAsync(member.AccessToken, communityId, a.HomeworkId, new(true, 0), Ct);
        Assert.True(done.Completed);
        var other = await service.GetCompletionAsync(member.AccessToken, communityId, b.HomeworkId, Ct);
        Assert.False(other.Completed);
        Assert.Equal(0, other.Revision);
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.shared_homework_completion WHERE homework_id='{a.HomeworkId}'"));
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.shared_homework_completion WHERE homework_id='{b.HomeworkId}'"));
    }

    [Fact]
    public async Task Unique_vote_before_deadline_second_and_late_rejected()
    {
        var clock = new AccountClock();
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, clock);
        var staff = await Seed(accounts, "poll.staff");
        var member = await Seed(accounts, "poll.member");
        var communityId = Guid.NewGuid();
        await db.SeedCommunityAsync(communityId);
        await db.SeedStaffAsync(communityId, staff.User.UserId);
        await db.SeedMemberAsync(communityId, member.User.UserId);
        var service = new CommunityService(accounts, db.Configuration);
        var poll = await service.PublishPollAsync(staff.AccessToken, communityId, new("Придете?", Deadline, ["Да", "Нет"], 0), Ct);
        var first = await service.VoteAsync(member.AccessToken, communityId, poll.PollId, new(poll.Options[0].OptionId), Ct);
        Assert.Equal(poll.Options[0].OptionId, first.OptionId);
        var second = await Assert.ThrowsAsync<CommunityServiceException>(() =>
            service.VoteAsync(member.AccessToken, communityId, poll.PollId, new(poll.Options[1].OptionId), Ct));
        Assert.Equal(409, second.Status);
        Assert.Equal("already_voted", second.Code);
        clock.Now = Deadline;
        var lateUser = await Seed(accounts, "poll.late");
        await db.SeedMemberAsync(communityId, lateUser.User.UserId);
        var late = await Assert.ThrowsAsync<CommunityServiceException>(() =>
            service.VoteAsync(lateUser.AccessToken, communityId, poll.PollId, new(poll.Options[0].OptionId), Ct));
        Assert.Equal(409, late.Status);
        Assert.Equal("poll_closed", late.Code);
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.votes"));
        var results = await service.ResultsAsync(member.AccessToken, communityId, poll.PollId, Ct);
        Assert.Equal(1, results.TotalVotes);
        Assert.DoesNotContain(results.Options, o => o.GetType().GetProperty("UserId") is not null);
        Assert.Equal(new[] { "label", "optionId", "votes" },
            typeof(PollOptionResult).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Join_request_accept_and_reject_and_staff_audit()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var staff = await Seed(accounts, "join.staff");
        var accepted = await Seed(accounts, "join.ok");
        var rejected = await Seed(accounts, "join.no");
        var communityId = Guid.NewGuid();
        await db.SeedCommunityAsync(communityId);
        await db.SeedStaffAsync(communityId, staff.User.UserId);
        var service = new CommunityService(accounts, db.Configuration);
        var ask = await service.RequestJoinAsync(accepted.AccessToken, communityId, Ct);
        var deny = await service.RequestJoinAsync(rejected.AccessToken, communityId, Ct);
        Assert.Equal("pending", ask.Status);
        var pending = await service.ListJoinRequestsAsync(staff.AccessToken, communityId, Ct);
        Assert.Equal(2, pending.Count);
        var member = await service.AcceptJoinAsync(staff.AccessToken, communityId, ask.RequestId, Ct);
        Assert.Equal("accepted", member.Status);
        var refused = await service.RejectJoinAsync(staff.AccessToken, communityId, deny.RequestId, Ct);
        Assert.Equal("rejected", refused.Status);
        var members = await service.ListMembersAsync(staff.AccessToken, communityId, Ct);
        Assert.Contains(members, m => m.UserId == accepted.User.UserId && m.Role == "member");
        Assert.DoesNotContain(members, m => m.UserId == rejected.User.UserId);
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.community_audit WHERE action='join_accepted'"));
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.community_audit WHERE action='join_rejected'"));
        var homework = await service.PublishHomeworkAsync(staff.AccessToken, communityId, new("ДЗ", "Текст", 0), Ct);
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.community_audit WHERE action='homework_published' AND object_id='{homework.HomeworkId}'"));
        var outsider = await Seed(accounts, "join.out");
        var forbidden = await Assert.ThrowsAsync<CommunityServiceException>(() =>
            service.AcceptJoinAsync(outsider.AccessToken, communityId, ask.RequestId, Ct));
        Assert.Equal(403, forbidden.Status);
    }

    [Fact]
    public async Task Accept_join_after_staff_seed_does_not_demote_role()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var staff = await Seed(accounts, "keep.staff");
        var promoted = await Seed(accounts, "keep.promoted");
        var communityId = Guid.NewGuid();
        await db.SeedCommunityAsync(communityId);
        await db.SeedStaffAsync(communityId, staff.User.UserId);
        var service = new CommunityService(accounts, db.Configuration);
        var pending = await service.RequestJoinAsync(promoted.AccessToken, communityId, Ct);
        await db.SeedStaffAsync(communityId, promoted.User.UserId, "curator");
        Assert.Equal("curator", await db.Accounts.ScalarAsync<string>($"SELECT role FROM {db.QuotedSchema}.memberships WHERE community_id='{communityId}' AND user_id='{promoted.User.UserId}'"));
        var accepted = await service.AcceptJoinAsync(staff.AccessToken, communityId, pending.RequestId, Ct);
        Assert.Equal("accepted", accepted.Status);
        Assert.Equal("curator", await db.Accounts.ScalarAsync<string>($"SELECT role FROM {db.QuotedSchema}.memberships WHERE community_id='{communityId}' AND user_id='{promoted.User.UserId}'"));
        Assert.Equal("active", await db.Accounts.ScalarAsync<string>($"SELECT status FROM {db.QuotedSchema}.memberships WHERE community_id='{communityId}' AND user_id='{promoted.User.UserId}'"));
        var members = await service.ListMembersAsync(staff.AccessToken, communityId, Ct);
        Assert.Contains(members, m => m.UserId == promoted.User.UserId && m.Role == "curator");
        await service.PublishHomeworkAsync(promoted.AccessToken, communityId, new("ДЗ", "Текст", 0), Ct);
    }

    [Fact]
    public async Task Expected_revision_conflict_and_session_revoke_are_401()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var staff = await Seed(accounts, "rev.staff");
        var communityId = Guid.NewGuid();
        await db.SeedCommunityAsync(communityId);
        await db.SeedStaffAsync(communityId, staff.User.UserId);
        var service = new CommunityService(accounts, db.Configuration);
        var homework = await service.PublishHomeworkAsync(staff.AccessToken, communityId, new("ДЗ", "Текст", 0), Ct);
        var conflict = await Assert.ThrowsAsync<CommunityServiceException>(() =>
            service.UpdateHomeworkAsync(staff.AccessToken, communityId, homework.HomeworkId, new("Другое", "Нет", 0), Ct));
        Assert.Equal(409, conflict.Status);
        Assert.Equal("revision_conflict", conflict.Code);
        await accounts.LogoutAsync(staff.AccessToken, Ct);
        var error = await Assert.ThrowsAsync<AccountServiceException>(() =>
            service.PublishHomeworkAsync(staff.AccessToken, communityId, new("После", "Выхода", 0), Ct));
        Assert.Equal(AccountFailure.InvalidSession, error.Failure);
    }
}
