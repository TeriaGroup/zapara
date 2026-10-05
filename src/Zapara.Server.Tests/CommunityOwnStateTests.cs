using Xunit;
using Zapara.Contracts.Communities;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class CommunityOwnStateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task New_pending_application_wins_over_a_resolved_request_with_the_same_timestamp()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db, clock: new AccountClock());
        var user = await Seed(host.Accounts, "same.timestamp");
        var community = Guid.NewGuid();
        await db.SeedCommunityAsync(community);
        var older = Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff");
        var pending = Guid.Parse("11111111-1111-4111-8111-111111111111");
        await db.Accounts.ExecuteAsync($"""
            INSERT INTO {db.QuotedSchema}.join_requests(request_id,community_id,user_id,status,created_at,resolved_at,resolved_by)
            VALUES('{older}','{community}','{user.User.UserId}','rejected','2026-09-08T12:00:00Z','2026-09-08T12:00:00Z','{user.User.UserId}'),
                  ('{pending}','{community}','{user.User.UserId}','pending','2026-09-08T12:00:00Z',NULL,NULL)
            """);
        var own = await host.Get<OwnJoinRequestResponse>($"/{community}/join-request", user.AccessToken);
        Assert.Equal(pending, own.Request!.RequestId);
        Assert.Equal("pending", own.Request.Status);
    }

    [Fact]
    public async Task Own_join_request_survives_reload_and_never_returns_another_applicants_request()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db, clock: new AccountClock());
        var staff = await Seed(host.Accounts, "status.staff");
        var a = await Seed(host.Accounts, "status.first");
        var b = await Seed(host.Accounts, "status.second");
        var community = Guid.NewGuid();
        await db.SeedCommunityAsync(community);
        await db.SeedStaffAsync(community, staff.User.UserId);
        Assert.Null((await host.Get<OwnJoinRequestResponse>($"/{community}/join-request", a.AccessToken)).Request);
        var first = CommunityJson.Parse<JoinRequestResponse>(await host.Send("POST", $"/{community}/join-requests", 201, a.AccessToken));
        var second = CommunityJson.Parse<JoinRequestResponse>(await host.Send("POST", $"/{community}/join-requests", 201, b.AccessToken));

        Assert.Equal(first.RequestId, (await host.Get<OwnJoinRequestResponse>($"/{community}/join-request", a.AccessToken)).Request!.RequestId);
        Assert.Equal(second.RequestId, (await host.Get<OwnJoinRequestResponse>($"/{community}/join-request", b.AccessToken)).Request!.RequestId);
        Assert.Null((await host.Get<OwnJoinRequestResponse>($"/{community}/join-request", staff.AccessToken)).Request);
        await host.Send("POST", $"/{community}/join-requests/{first.RequestId}/reject", 200, staff.AccessToken);
        await host.Send("POST", $"/{community}/join-requests/{second.RequestId}/accept", 200, staff.AccessToken);
        Assert.Equal("rejected", (await host.Get<OwnJoinRequestResponse>($"/{community}/join-request", a.AccessToken)).Request!.Status);
        Assert.Equal("accepted", (await host.Get<OwnJoinRequestResponse>($"/{community}/join-request", b.AccessToken)).Request!.Status);
        await host.Problem("GET", $"/{community}/join-request?userId={b.User.UserId}", 400, "invalid_request", a.AccessToken);
        await host.Problem("GET", $"/{community}/join-requests", 403, "forbidden", a.AccessToken);
    }

    [Fact]
    public async Task Own_vote_reloads_only_the_callers_choice_and_requires_active_membership()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db, clock: new AccountClock());
        var staff = await Seed(host.Accounts, "vote.status.staff");
        var a = await Seed(host.Accounts, "vote.status.first");
        var b = await Seed(host.Accounts, "vote.status.second");
        var community = Guid.NewGuid();
        await db.SeedCommunityAsync(community);
        await db.SeedStaffAsync(community, staff.User.UserId);
        await db.SeedMemberAsync(community, a.User.UserId);
        await db.SeedMemberAsync(community, b.User.UserId);
        var poll = CommunityJson.Parse<PollResponse>(await host.Send("POST", $"/{community}/polls", 201, staff.AccessToken,
            CommunityJson.Serialize(new PollUpsert("Выберите время", new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero), ["Утром", "Вечером"], 0))));
        Assert.Null((await host.Get<OwnVoteResponse>($"/{community}/polls/{poll.PollId}/vote", a.AccessToken)).Vote);
        await host.Send("POST", $"/{community}/polls/{poll.PollId}/votes", 201, a.AccessToken, CommunityJson.Serialize(new VoteRequest(poll.Options[1].OptionId)));

        Assert.Equal(poll.Options[1].OptionId, (await host.Get<OwnVoteResponse>($"/{community}/polls/{poll.PollId}/vote", a.AccessToken)).Vote!.OptionId);
        Assert.Null((await host.Get<OwnVoteResponse>($"/{community}/polls/{poll.PollId}/vote", b.AccessToken)).Vote);
        Assert.Null((await host.Get<OwnVoteResponse>($"/{community}/polls/{poll.PollId}/vote", staff.AccessToken)).Vote);
        await host.Problem("GET", $"/{community}/polls/{poll.PollId}/vote?userId={a.User.UserId}", 400, "invalid_request", b.AccessToken);
        await db.Accounts.ExecuteAsync($"UPDATE {db.QuotedSchema}.memberships SET status='revoked',revoked_at=now() WHERE community_id='{community}' AND user_id='{a.User.UserId}'");
        await host.Problem("GET", $"/{community}/polls/{poll.PollId}/vote", 403, "forbidden", a.AccessToken);
        await host.Problem("GET", $"/{community}/polls/{Guid.NewGuid()}/vote", 404, "not_found", b.AccessToken);
    }
}
