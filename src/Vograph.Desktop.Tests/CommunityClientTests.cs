using System.Net;
using System.Text.Json;
using Vograph.Core.Services.Communities;
using Xunit;
using Zapara.Contracts.Communities;
using static Vograph.Desktop.Tests.AccountClientTestSupport;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed partial class CommunityClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Member_and_staff_routes_use_exact_prefix_json_and_per_request_bearer()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root, new AccountClientClock());
        var expected = new Queue<(string Method, string Path, object? Body, object Response, HttpStatusCode Status)>([
            ("GET", "", null, new[] { Membership }, HttpStatusCode.OK),
            ("GET", "?groupId=O3313", null, new[] { Catalog }, HttpStatusCode.OK),
            ("GET", $"/{CommunityId:D}", null, Membership, HttpStatusCode.OK),
            ("POST", $"/{CommunityId:D}/join-requests", null, Pending, HttpStatusCode.Created),
            ("GET", $"/{CommunityId:D}/join-requests", null, new[] { Pending }, HttpStatusCode.OK),
            ("POST", $"/{CommunityId:D}/join-requests/{RequestId:D}/accept", null, Accepted, HttpStatusCode.OK),
            ("POST", $"/{CommunityId:D}/join-requests/{RequestId:D}/reject", null, Rejected, HttpStatusCode.OK),
            ("GET", $"/{CommunityId:D}/members", null, Members, HttpStatusCode.OK),
            ("GET", $"/{CommunityId:D}/staff", null, Staff, HttpStatusCode.OK),
            ("GET", $"/{CommunityId:D}/homework", null, new[] { Homework }, HttpStatusCode.OK),
            ("POST", $"/{CommunityId:D}/homework", HomeworkWrite, Homework, HttpStatusCode.Created),
            ("GET", $"/{CommunityId:D}/homework/{HomeworkId:D}", null, Homework, HttpStatusCode.OK),
            ("PUT", $"/{CommunityId:D}/homework/{HomeworkId:D}", HomeworkWrite, Homework, HttpStatusCode.OK),
            ("GET", $"/{CommunityId:D}/homework/{HomeworkId:D}/completion", null, Completion, HttpStatusCode.OK),
            ("PUT", $"/{CommunityId:D}/homework/{HomeworkId:D}/completion", CompletionWrite, Completion, HttpStatusCode.OK),
            ("GET", $"/{CommunityId:D}/announcements", null, new[] { Announcement }, HttpStatusCode.OK),
            ("POST", $"/{CommunityId:D}/announcements", AnnouncementWrite, Announcement, HttpStatusCode.Created),
            ("PUT", $"/{CommunityId:D}/announcements/{AnnouncementId:D}", AnnouncementWrite, Announcement, HttpStatusCode.OK),
            ("GET", $"/{CommunityId:D}/polls", null, new[] { Poll }, HttpStatusCode.OK),
            ("POST", $"/{CommunityId:D}/polls", PollWrite, Poll, HttpStatusCode.Created),
            ("GET", $"/{CommunityId:D}/polls/{PollId:D}", null, Poll, HttpStatusCode.OK),
            ("POST", $"/{CommunityId:D}/polls/{PollId:D}/votes", VoteWrite, Vote, HttpStatusCode.Created),
            ("GET", $"/{CommunityId:D}/polls/{PollId:D}/results", null, Results, HttpStatusCode.OK)
        ]);
        handler.Send = async (request, ct) =>
        {
            var e = expected.Dequeue();
            Assert.Equal(e.Method, request.Method.Method);
            Assert.Equal("https://example.invalid/root/api/v1/communities" + e.Path, request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer " + Access, request.Headers.Authorization?.ToString());
            Assert.Null(http.DefaultRequestHeaders.Authorization);
            Assert.Single(request.Headers.Accept, h => h.MediaType == "application/json");
            if (e.Body is null) Assert.Null(request.Content);
            else
            {
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
                Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(e.Body, CommunityJson.CreateOptions()),
                    await request.Content.ReadAsByteArrayAsync(ct));
            }
            return Payload(e.Response, e.Status);
        };
        Equal(Membership, Assert.Single(await client.ListAsync(Access, ct: Ct)));
        Assert.Null(Assert.Single(await client.ListAsync(Access, "O3313", Ct)).Role);
        Equal(Membership, await client.GetAsync(Access, CommunityId, Ct));
        Equal(Pending, await client.RequestJoinAsync(Access, CommunityId, Ct));
        Equal(Pending, Assert.Single(await client.ListJoinRequestsAsync(Access, CommunityId, Ct)));
        Equal(Accepted, await client.AcceptJoinAsync(Access, CommunityId, RequestId, Ct));
        Equal(Rejected, await client.RejectJoinAsync(Access, CommunityId, RequestId, Ct));
        Equal(Members, (await client.ListMembersAsync(Access, CommunityId, Ct)).ToArray());
        Equal(Staff, (await client.ListStaffAsync(Access, CommunityId, Ct)).ToArray());
        Equal(Homework, Assert.Single(await client.ListHomeworkAsync(Access, CommunityId, Ct)));
        Equal(Homework, await client.PublishHomeworkAsync(Access, CommunityId, HomeworkWrite, Ct));
        Equal(Homework, await client.GetHomeworkAsync(Access, CommunityId, HomeworkId, Ct));
        Equal(Homework, await client.UpdateHomeworkAsync(Access, CommunityId, HomeworkId, HomeworkWrite, Ct));
        Equal(Completion, await client.GetCompletionAsync(Access, CommunityId, HomeworkId, Ct));
        Equal(Completion, await client.UpsertCompletionAsync(Access, CommunityId, HomeworkId, CompletionWrite, Ct));
        Equal(Announcement, Assert.Single(await client.ListAnnouncementsAsync(Access, CommunityId, Ct)));
        Equal(Announcement, await client.PublishAnnouncementAsync(Access, CommunityId, AnnouncementWrite, Ct));
        Equal(Announcement, await client.UpdateAnnouncementAsync(Access, CommunityId, AnnouncementId, AnnouncementWrite, Ct));
        Equal(Poll, Assert.Single(await client.ListPollsAsync(Access, CommunityId, Ct)));
        Equal(Poll, await client.PublishPollAsync(Access, CommunityId, PollWrite, Ct));
        Equal(Poll, await client.GetPollAsync(Access, CommunityId, PollId, Ct));
        Equal(Vote, await client.VoteAsync(Access, CommunityId, PollId, VoteWrite, Ct));
        Equal(Results, await client.ResultsAsync(Access, CommunityId, PollId, Ct));
        Assert.Empty(expected);
        Assert.Empty(http.DefaultRequestHeaders);
    }

    [Fact]
    public async Task List_omits_or_encodes_optional_groupId_without_granting_a_role()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        handler.Send = (request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://example.invalid/root/api/v1/communities?groupId=O3313", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Payload(new[] { Catalog }));
        };
        var catalog = Assert.Single(await client.ListAsync(Access, "O3313", Ct));
        Assert.Null(catalog.Role);
        handler.Send = (request, _) =>
        {
            Assert.Equal("https://example.invalid/root/api/v1/communities", request.RequestUri!.AbsoluteUri);
            Assert.Equal("", request.RequestUri.Query);
            return Task.FromResult(Payload(Array.Empty<CommunityResponse>()));
        };
        Assert.Empty(await client.ListAsync(Access, ct: Ct));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Join_uses_created_and_accept_returns_payload_without_rewriting_staff_roles()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        handler.Send = (request, _) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/join-requests", StringComparison.Ordinal))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Null(request.Content);
                return Task.FromResult(Payload(Pending, HttpStatusCode.Created));
            }
            if (request.RequestUri!.AbsolutePath.EndsWith($"/join-requests/{RequestId:D}/accept", StringComparison.Ordinal))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Null(request.Content);
                return Task.FromResult(Payload(Accepted));
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.EndsWith($"/{CommunityId:D}/members", request.RequestUri.AbsolutePath, StringComparison.Ordinal);
            return Task.FromResult(Payload(Members));
        };
        var joined = await client.RequestJoinAsync(Access, CommunityId, Ct);
        Assert.Equal("pending", joined.Status);
        Assert.Null(typeof(JoinRequestResponse).GetProperty("Role"));
        var accepted = await client.AcceptJoinAsync(Access, CommunityId, RequestId, Ct);
        Equal(Accepted, accepted);
        Assert.Equal("accepted", accepted.Status);
        var members = await client.ListMembersAsync(Access, CommunityId, Ct);
        Assert.Contains(members, m => m.UserId == PromotedId && m.Role == "curator");
        Assert.Contains(members, m => m.UserId == StaffId && m.Role == "headman");
        Assert.DoesNotContain(members, m => m.UserId == PromotedId && m.Role == "member");
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Member_publish_is_forbidden_and_not_retried()
    {
        using var handler = new AccountClientHandler
        {
            Send = (_, _) => Task.FromResult(Problem(403, "forbidden"))
        };
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        var error = await Assert.ThrowsAsync<CommunityClientException>(
            () => client.PublishHomeworkAsync(Access, CommunityId, HomeworkWrite, Ct));
        Assert.Equal(CommunityClientFailure.Forbidden, error.Failure);
        Assert.Equal(403, error.Status);
        Assert.Equal("forbidden", error.Code);
        Assert.DoesNotContain(Password, error.ToString());
        Assert.Null(error.InnerException);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Vote_is_unique_and_results_expose_aggregates_without_individual_votes()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        handler.Send = (request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                Assert.EndsWith($"/polls/{PollId:D}/votes", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                return Task.FromResult(handler.Calls == 1
                    ? Payload(Vote, HttpStatusCode.Created)
                    : Problem(409, "already_voted"));
            }
            Assert.EndsWith($"/polls/{PollId:D}/results", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            var payload = CommunityJson.Serialize(Results);
            using (var document = JsonDocument.Parse(payload))
            {
                var root = document.RootElement;
                Assert.Equal(new[] { "options", "pollId", "totalVotes" }, root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
                foreach (var option in root.GetProperty("options").EnumerateArray())
                    Assert.Equal(new[] { "label", "optionId", "votes" }, option.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            }
            Assert.DoesNotContain(UserId.ToString("D"), System.Text.Encoding.UTF8.GetString(payload));
            Assert.DoesNotContain("userId", System.Text.Encoding.UTF8.GetString(payload), StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(Payload(Results));
        };
        Equal(Vote, await client.VoteAsync(Access, CommunityId, PollId, VoteWrite, Ct));
        var second = await Assert.ThrowsAsync<CommunityClientException>(
            () => client.VoteAsync(Access, CommunityId, PollId, new(OptionNo), Ct));
        Assert.Equal(CommunityClientFailure.AlreadyVoted, second.Failure);
        Assert.Equal(409, second.Status);
        Assert.Equal("already_voted", second.Code);
        var results = await client.ResultsAsync(Access, CommunityId, PollId, Ct);
        Assert.Equal(2, results.TotalVotes);
        Assert.Equal(2, results.Options.Count);
        Assert.Equal(results.TotalVotes, results.Options.Sum(o => o.Votes));
        Assert.Null(typeof(PollResultsResponse).GetProperty("Voters"));
        Assert.Null(typeof(PollResultsResponse).GetProperty("Votes"));
        Assert.Null(typeof(PollOptionResult).GetProperty("UserId"));
        Assert.Equal(3, handler.Calls);
    }
}
