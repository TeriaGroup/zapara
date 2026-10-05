using System.Text;
using System.Text.Json;
using Xunit;
using Zapara.Contracts.Communities;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class CommunityApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateTimeOffset Deadline => new(2026, 9, 8, 12, 10, 0, TimeSpan.Zero);
    private static byte[] Json<T>(T value) => CommunityJson.Serialize(value);

    [Fact]
    public async Task Catalog_http_selection_grants_no_role()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var token = (await Seed(host.Accounts, "http.catalog")).AccessToken;
        var communityId = Guid.NewGuid();
        await db.SeedCommunityAsync(communityId);
        await db.SeedCatalogAsync(communityId);
        var list = await host.Get<CommunityResponse[]>("?groupId=O3313", token);
        Assert.Single(list);
        Assert.Null(list[0].Role);
        await host.Problem("POST", $"/{communityId}/homework", 403, "forbidden", token, Json(new HomeworkUpsert("ДЗ", "Текст", 0)));
        await host.Problem("GET", $"/{communityId}/members", 403, "forbidden", token);
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.memberships"));
    }

    [Fact]
    public async Task Member_results_hide_individual_votes_and_votes_list_is_absent()
    {
        var clock = new AccountClock();
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db, clock: clock);
        var staff = await Seed(host.Accounts, "http.staff");
        var a = await Seed(host.Accounts, "http.voter.a");
        var b = await Seed(host.Accounts, "http.voter.b");
        var communityId = Guid.NewGuid();
        await db.SeedCommunityAsync(communityId);
        await db.SeedStaffAsync(communityId, staff.User.UserId);
        await db.SeedMemberAsync(communityId, a.User.UserId);
        await db.SeedMemberAsync(communityId, b.User.UserId);
        var poll = CommunityJson.Parse<PollResponse>(await host.Send("POST", $"/{communityId}/polls", 201, staff.AccessToken,
            Json(new PollUpsert("Придете?", Deadline, ["Да", "Нет"], 0))));
        await host.Send("POST", $"/{communityId}/polls/{poll.PollId}/votes", 201, a.AccessToken, Json(new VoteRequest(poll.Options[0].OptionId)));
        await host.Send("POST", $"/{communityId}/polls/{poll.PollId}/votes", 201, b.AccessToken, Json(new VoteRequest(poll.Options[1].OptionId)));
        var resultsBytes = await host.Send("GET", $"/{communityId}/polls/{poll.PollId}/results", 200, a.AccessToken);
        var results = CommunityJson.Parse<PollResultsResponse>(resultsBytes);
        Assert.Equal(2, results.TotalVotes);
        using (var document = JsonDocument.Parse(resultsBytes))
        {
            ApiTestFactory.Keys(document.RootElement, "pollId", "totalVotes", "options");
            foreach (var option in document.RootElement.GetProperty("options").EnumerateArray())
                ApiTestFactory.Keys(option, "optionId", "label", "votes");
        }
        var text = Encoding.UTF8.GetString(resultsBytes);
        Assert.DoesNotContain(a.User.UserId.ToString("D"), text);
        Assert.DoesNotContain(b.User.UserId.ToString("D"), text);
        using var votes = await host.Client.GetAsync($"/api/v1/communities/{communityId}/polls/{poll.PollId}/votes", Ct);
        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, votes.StatusCode);
        await host.Problem("POST", $"/{communityId}/polls/{poll.PollId}/votes", 409, "already_voted", a.AccessToken,
            Json(new VoteRequest(poll.Options[1].OptionId)));
        clock.Now = Deadline;
        var late = await Seed(host.Accounts, "http.voter.late");
        await db.SeedMemberAsync(communityId, late.User.UserId);
        await host.Problem("POST", $"/{communityId}/polls/{poll.PollId}/votes", 409, "poll_closed", late.AccessToken,
            Json(new VoteRequest(poll.Options[0].OptionId)));
    }

    [Fact]
    public async Task Completion_http_is_per_homework_and_join_accept_reject()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var staff = await Seed(host.Accounts, "http.head");
        var member = await Seed(host.Accounts, "http.mem");
        var denied = await Seed(host.Accounts, "http.deny");
        var communityId = Guid.NewGuid();
        await db.SeedCommunityAsync(communityId);
        await db.SeedStaffAsync(communityId, staff.User.UserId);
        var ask = CommunityJson.Parse<JoinRequestResponse>(await host.Send("POST", $"/{communityId}/join-requests", 201, member.AccessToken));
        var no = CommunityJson.Parse<JoinRequestResponse>(await host.Send("POST", $"/{communityId}/join-requests", 201, denied.AccessToken));
        await host.Send("POST", $"/{communityId}/join-requests/{ask.RequestId}/accept", 200, staff.AccessToken);
        await host.Send("POST", $"/{communityId}/join-requests/{no.RequestId}/reject", 200, staff.AccessToken);
        var members = await host.Get<MemberResponse[]>($"/{communityId}/members", staff.AccessToken);
        Assert.Contains(members, m => m.UserId == member.User.UserId);
        Assert.DoesNotContain(members, m => m.UserId == denied.User.UserId);
        var a = CommunityJson.Parse<HomeworkResponse>(await host.Send("POST", $"/{communityId}/homework", 201, staff.AccessToken, Json(new HomeworkUpsert("А", "Текст А", 0))));
        var b = CommunityJson.Parse<HomeworkResponse>(await host.Send("POST", $"/{communityId}/homework", 201, staff.AccessToken, Json(new HomeworkUpsert("Б", "Текст Б", 0))));
        await host.Send("PUT", $"/{communityId}/homework/{a.HomeworkId}/completion", 200, member.AccessToken, Json(new CompletionUpsert(true, 0)));
        var other = await host.Get<CompletionResponse>($"/{communityId}/homework/{b.HomeworkId}/completion", member.AccessToken);
        Assert.False(other.Completed);
        await host.Problem("PUT", $"/{communityId}/homework/{a.HomeworkId}", 409, "revision_conflict", staff.AccessToken, Json(new HomeworkUpsert("А", "Смена", 0)));
        await db.RevokeStaffAsync(communityId, staff.User.UserId);
        await host.Problem("POST", $"/{communityId}/announcements", 403, "forbidden", staff.AccessToken, Json(new AnnouncementUpsert("Нет", "Текст", 0)));
        await host.Accounts.LogoutAsync(member.AccessToken, Ct);
        await host.Problem("GET", $"/{communityId}/homework", 401, "invalid_session", member.AccessToken);
        Assert.Equal(2L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.community_audit WHERE action='homework_published'"));
    }

    [Fact]
    public async Task Disabled_module_routes_are_absent()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db, mapCommunities: false);
        var token = (await Seed(host.Accounts, "http.off")).AccessToken;
        using var response = await host.Client.GetAsync("/api/v1/communities", Ct);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        using var homework = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/communities/" + Guid.NewGuid().ToString("D") + "/homework")
        {
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token) },
            Content = new ByteArrayContent(Json(new HomeworkUpsert("ДЗ", "Текст", 0))) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") } }
        }, Ct);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, homework.StatusCode);
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.shared_homework"));
    }

    [Fact]
    public async Task Anonymous_is_401_and_http_never_migrates_empty_schema()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync();
        await using var host = await CommunityApiTestHost.StartAsync(db);
        await host.Problem("GET", "", 401, "invalid_session");
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}'"));
    }
}
