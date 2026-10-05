using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Communities;
using Xunit;
using Zapara.Contracts.Communities;
using static Vograph.Desktop.Tests.AccountClientTestSupport;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed partial class CommunityClientTests
{
    [Theory]
    [InlineData(400, "invalid_request", CommunityClientFailure.InvalidRequest)]
    [InlineData(401, "invalid_session", CommunityClientFailure.InvalidSession)]
    [InlineData(403, "forbidden", CommunityClientFailure.Forbidden)]
    [InlineData(404, "not_found", CommunityClientFailure.NotFound)]
    [InlineData(409, "revision_conflict", CommunityClientFailure.RevisionConflict)]
    [InlineData(409, "already_voted", CommunityClientFailure.AlreadyVoted)]
    [InlineData(409, "already_member", CommunityClientFailure.AlreadyMember)]
    [InlineData(409, "already_requested", CommunityClientFailure.AlreadyRequested)]
    [InlineData(409, "poll_closed", CommunityClientFailure.PollClosed)]
    [InlineData(413, "payload_too_large", CommunityClientFailure.PayloadTooLarge)]
    [InlineData(415, "invalid_request", CommunityClientFailure.InvalidRequest)]
    [InlineData(429, "rate_limited", CommunityClientFailure.RateLimited)]
    [InlineData(503, "db_unavailable", CommunityClientFailure.DbUnavailable)]
    [InlineData(500, "internal_error", CommunityClientFailure.InternalError)]
    [InlineData(401, "forbidden", CommunityClientFailure.ServerUnavailable)]
    public async Task Errors_are_allowlisted_sanitized_and_never_retried(int status, string code, CommunityClientFailure failure)
    {
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(Problem(status, code)) };
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        var error = await Assert.ThrowsAsync<CommunityClientException>(() => client.ListAsync(Access, ct: Ct));
        Assert.Equal(failure, error.Failure);
        Assert.Equal(status, error.Status);
        Assert.DoesNotContain(Password, error.ToString());
        Assert.Null(error.InnerException);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("http://example.invalid/")]
    [InlineData("https://user:password@example.invalid/")]
    [InlineData("https://example.invalid/?secret=x")]
    [InlineData("https://example.invalid/#x")]
    [InlineData("/relative")]
    public void Invalid_base_is_rejected_without_echo(string uri)
    {
        using var http = new HttpClient();
        var error = Assert.Throws<ArgumentException>(() => new CommunityHttpClient(http, new(uri, UriKind.RelativeOrAbsolute)));
        Assert.DoesNotContain(uri, error.ToString());
    }

    [Fact]
    public async Task Request_shape_guards_prevent_network_and_default_headers_are_not_accepted()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        await Assert.ThrowsAsync<ArgumentException>(() => client.ListAsync("invalid", ct: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetAsync(Access, Guid.Empty, Ct));
        var invalid = await Assert.ThrowsAsync<CommunityClientException>(() => client.ListAsync(Access, "\0group", Ct));
        Assert.Equal(CommunityClientFailure.InvalidRequest, invalid.Failure);
        await Assert.ThrowsAsync<CommunityClientException>(() => client.PublishHomeworkAsync(Access, CommunityId, null!, Ct));
        http.DefaultRequestHeaders.Authorization = new("Bearer", "unrelated");
        Assert.Throws<ArgumentException>(() => new CommunityHttpClient(http, Root));
        await Assert.ThrowsAsync<CommunityClientException>(() => client.ListAsync(Access, ct: Ct));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("http://localhost:1234/prefix")]
    [InlineData("http://127.0.0.2:1234/prefix")]
    [InlineData("http://[::1]:1234/prefix")]
    [InlineData("https://example.invalid/prefix")]
    public void Scope_canonicalizes_root_and_owned_client_matches(string url)
    {
        var scope = new AccountServerScope(new(url));
        using var owned = CommunityHttpClient.CreateOwned(new(url));
        Assert.Equal(scope.Key, owned.Scope.Key);
        Assert.Equal(scope.Key, new AccountServerScope(new(url + "/")).Key);
    }

    [Fact]
    public async Task Injected_client_survives_adapter_disposal()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        new CommunityHttpClient(http, Root).Dispose();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/");
        using var response = await http.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(200)]
    public async Task Join_created_is_required_and_redirect_is_not_replayed(int status)
    {
        using var handler = new AccountClientHandler { Send = (_, _) =>
        {
            var response = status == 200 ? Payload(Pending) : TextPayload("", (HttpStatusCode)status);
            response.Headers.Location = new("https://other.invalid/" + Password);
            return Task.FromResult(response);
        } };
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        var error = await Assert.ThrowsAsync<CommunityClientException>(() => client.RequestJoinAsync(Access, CommunityId, Ct));
        Assert.DoesNotContain(Password, error.ToString());
        Assert.Equal(1, handler.Calls);
        Assert.NotEqual(CommunityClientFailure.AlreadyMember, error.Failure);
    }

    [Theory]
    [InlineData("voters")]
    [InlineData("option-user")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public async Task Results_reject_individual_votes_and_malformed_wire(string mode)
    {
        var node = JsonNode.Parse(CommunityJson.Serialize(Results))!.AsObject();
        var text = node.ToJsonString();
        if (mode == "voters") node["voters"] = new JsonArray(UserId.ToString("D"));
        if (mode == "option-user") node["options"]![0]!.AsObject()["userId"] = UserId.ToString("D");
        if (mode == "missing") node.Remove("totalVotes");
        text = mode == "duplicate" ? text.Insert(1, "\"pollId\":\"" + PollId.ToString("D") + "\",") : node.ToJsonString();
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(TextPayload(mode == "duplicate" ? text : node.ToJsonString())) };
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        var error = await Assert.ThrowsAsync<CommunityClientException>(() => client.ResultsAsync(Access, CommunityId, PollId, Ct));
        Assert.Equal(CommunityClientFailure.InvalidPayload, error.Failure);
        Assert.DoesNotContain(UserId.ToString("D"), error.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Success_and_error_bodies_are_counted_even_without_length(bool errorResponse, bool chunked)
    {
        var limit = errorResponse ? 4096 : 65536;
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(new HttpResponseMessage(
            errorResponse ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
        { Content = chunked ? new AccountUnknownLengthContent(new byte[limit + 1]) : new ByteArrayContent(new byte[limit + 1]) }) };
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        var error = await Assert.ThrowsAsync<CommunityClientException>(() => client.ListAsync(Access, ct: Ct));
        Assert.Equal(CommunityClientFailure.BodyTooLarge, error.Failure);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("60", 60)]
    [InlineData("999999", 300)]
    [InlineData("-1", -1)]
    [InlineData("not-a-date", -1)]
    [InlineData("Tue, 08 Sep 2026 12:02:00 GMT", 120)]
    public async Task Retry_after_is_bounded(string value, int seconds)
    {
        using var handler = new AccountClientHandler { Send = (_, _) =>
        {
            var response = Problem(429, "rate_limited");
            response.Headers.TryAddWithoutValidation("Retry-After", value);
            return Task.FromResult(response);
        } };
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root, new AccountClientClock());
        var error = await Assert.ThrowsAsync<CommunityClientException>(() => client.ListAsync(Access, ct: Ct));
        Assert.Equal(seconds < 0 ? null : TimeSpan.FromSeconds(seconds), error.RetryAfter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_and_overall_timeout_do_not_leak_or_retry(bool caller)
    {
        var clock = new AccountClientClock();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var handler = new AccountClientHandler { Send = async (_, ct) =>
        {
            if (caller) cancel.Cancel(); else clock.Expire();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Payload(new[] { Membership });
        } };
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root, clock);
        if (caller) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ListAsync(Access, ct: cancel.Token));
        else Assert.Equal(CommunityClientFailure.Timeout, (await Assert.ThrowsAsync<CommunityClientException>(
            () => client.ListAsync(Access, ct: cancel.Token))).Failure);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Transport_and_content_encoding_are_redacted_without_retry()
    {
        using var handler = new AccountClientHandler { Send = (_, _) => throw new HttpRequestException(Password + Access) };
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        var error = await Assert.ThrowsAsync<CommunityClientException>(() => client.ListAsync(Access, ct: Ct));
        Assert.Equal(CommunityClientFailure.Transport, error.Failure);
        Assert.DoesNotContain(Password, error.ToString());
        Assert.DoesNotContain(Access, error.ToString());
        Assert.Null(error.InnerException);
        handler.Send = (_, _) =>
        {
            var response = Payload(new[] { Membership });
            response.Content.Headers.ContentEncoding.Add("gzip");
            return Task.FromResult(response);
        };
        Assert.Equal(CommunityClientFailure.InvalidPayload, (await Assert.ThrowsAsync<CommunityClientException>(
            () => client.ListAsync(Access, ct: Ct))).Failure);
        Assert.Equal(2, handler.Calls);
    }
}
