using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public partial class AccountClientTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("null")]
    [InlineData("token")]
    [InlineData("timestamp")]
    [InlineData("duplicate")]
    public async Task Session_response_must_be_strict_and_valid(string mode)
    {
        var node = JsonSerializer.SerializeToNode(Session(), AccountJson.CreateOptions())!.AsObject();
        switch (mode)
        {
            case "missing": node.Remove("refreshToken"); break;
            case "unknown": node["secret"] = Password; break;
            case "null": node["user"] = null; break;
            case "token": node["accessToken"] = Token("zr_"); break;
            case "timestamp": node["accessExpiresAt"] = Now.AddDays(31); break;
        }
        var text = node.ToJsonString();
        if (mode == "duplicate") text = text.Insert(1, "\"tokenType\":\"Bearer\",");
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(Raw(text)) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        var error = await Assert.ThrowsAsync<AccountClientException>(() => client.LoginAsync(Login, Ct));
        Assert.Equal(AccountClientFailure.InvalidPayload, error.Failure);
        Assert.DoesNotContain(Password, error.ToString());
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
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        var error = await Assert.ThrowsAsync<AccountClientException>(() => client.LoginAsync(Login, Ct));
        Assert.Equal(AccountClientFailure.BodyTooLarge, error.Failure);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("60", 60)]
    [InlineData("999999", 300)]
    [InlineData("-1", -1)]
    [InlineData("not-a-date", -1)]
    [InlineData("Tue, 08 Sep 2026 12:02:00 GMT", 120)]
    public async Task Retry_after_is_bounded_and_untrusted_headers_do_not_authenticate(string value, int seconds)
    {
        using var handler = new AccountClientHandler { Send = (_, _) =>
        {
            var response = Json(new { title = Password, status = 429, code = "rate_limited" }, HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("Retry-After", value);
            response.Headers.TryAddWithoutValidation("X-Authenticated-User", UserId.ToString());
            return Task.FromResult(response);
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"), new AccountClientClock());
        var error = await Assert.ThrowsAsync<AccountClientException>(() => client.LoginAsync(Login, Ct));
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
            return Json(Session());
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"), clock);
        if (caller) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RefreshAsync(Token("zr_"), cancel.Token));
        else Assert.Equal(AccountClientFailure.Timeout, (await Assert.ThrowsAsync<AccountClientException>(
            () => client.RefreshAsync(Token("zr_"), cancel.Token))).Failure);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Transport_exception_is_redacted_and_no_POST_retry()
    {
        using var handler = new AccountClientHandler { Send = (_, _) => throw new HttpRequestException(Password + Token("zr_")) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        var error = await Assert.ThrowsAsync<AccountClientException>(() => client.RefreshAsync(Token("zr_"), Ct));
        Assert.Equal(AccountClientFailure.Transport, error.Failure);
        Assert.DoesNotContain(Password, error.ToString());
        Assert.Null(error.InnerException);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"user\":null,\"familyId\":\"20000000-0000-0000-0000-000000000001\",\"authenticationMethods\":[]}")]
    public async Task Me_does_not_accept_missing_or_null_required_fields(string json)
    {
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(Raw(json)) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("http://127.0.0.1:1234/prefix"));
        Assert.Equal(AccountClientFailure.InvalidPayload, (await Assert.ThrowsAsync<AccountClientException>(
            () => client.GetMeAsync(Token("za_"), Ct))).Failure);
    }
}

internal sealed class AccountUnknownLengthContent(byte[] bytes) : HttpContent
{
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
}
