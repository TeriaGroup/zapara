using System.Net;
using System.Text;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Sync;
using Xunit;
using Zapara.Contracts.Sync;

namespace Vograph.Desktop.Tests;

public sealed partial class PrivateSyncClientTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("utf8")]
    [InlineData("depth")]
    [InlineData("plain")]
    public async Task Malformed_payloads_are_invalid_and_do_not_print_bodies(string kind)
    {
        byte[] body = kind switch
        {
            "missing" => Encoding.UTF8.GetBytes("{\"syncEpoch\":\"11111111-1111-1111-1111-111111111111\",\"currentSequence\":0}"),
            "unknown" => Encoding.UTF8.GetBytes("{\"syncEpoch\":\"11111111-1111-1111-1111-111111111111\",\"currentSequence\":0,\"minAfterSequence\":0,\"secret\":1}"),
            "duplicate" => Encoding.UTF8.GetBytes("{\"syncEpoch\":\"11111111-1111-1111-1111-111111111111\",\"currentSequence\":0,\"minAfterSequence\":0,\"minAfterSequence\":0}"),
            "utf8" => [0xff, 0xfe],
            "depth" => Encoding.UTF8.GetBytes(new string('[', 17) + new string(']', 17)),
            _ => Encoding.UTF8.GetBytes("not json")
        };
        using var http = new HttpClient(new Script((_, _) => Task.FromResult(Raw(body))));
        using var client = Client(http);
        var result = await client.MetadataAsync(Access);
        Assert.Equal(PrivateSyncState.InvalidResponse, result.State);
        Assert.DoesNotContain(Access, result.ToString());
        Assert.DoesNotContain("secret", result.Diagnostic);
    }

    [Theory]
    [InlineData(false, false, 65536)]
    [InlineData(false, true, 65536)]
    [InlineData(true, false, 4096)]
    [InlineData(true, true, 4096)]
    public async Task Bodies_are_counted_with_or_without_length(bool error, bool chunked, int limit)
    {
        using var http = new HttpClient(new Script((_, _) =>
        {
            HttpContent content = chunked ? new PrivateSyncUnknownLengthContent(new byte[limit + 1]) : new ByteArrayContent(new byte[limit + 1]);
            content.Headers.ContentType = new("application/json");
            return Task.FromResult(new HttpResponseMessage(error ? HttpStatusCode.BadRequest : HttpStatusCode.OK) { Content = content });
        }));
        using var client = Client(http);
        Assert.Equal(PrivateSyncState.InvalidResponse, (await client.MetadataAsync(Access)).State);
    }

    [Fact]
    public async Task Page_body_bound_is_eight_mebibytes()
    {
        using var http = new HttpClient(new Script((_, _) =>
        {
            var content = new ByteArrayContent(new byte[8 * 1024 * 1024 + 1]);
            content.Headers.ContentType = new("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }));
        using var client = Client(http);
        Assert.Equal(PrivateSyncState.InvalidResponse, (await client.ChangesAsync(Access, Epoch, 0)).State);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(204)]
    public async Task Redirect_empty_or_non_json_success_is_invalid_without_retry(int status)
    {
        var count = 0;
        using var http = new HttpClient(new Script((_, _) =>
        {
            count++;
            var response = Raw([], status);
            response.Headers.Location = new("https://other.invalid/" + Access);
            return Task.FromResult(response);
        }));
        using var client = Client(http);
        var result = await client.MutateAsync(Access, Mutation);
        Assert.Equal(PrivateSyncState.InvalidResponse, result.State);
        Assert.Equal(1, count);
        Assert.DoesNotContain(Access, result.ToString());
    }

    [Fact]
    public async Task Transport_failure_is_unavailable_without_post_retry_or_token_echo()
    {
        var count = 0;
        using var http = new HttpClient(new Script((_, _) =>
        {
            count++;
            throw new HttpRequestException(Access);
        }));
        using var client = Client(http);
        var result = await client.MutateAsync(Access, Mutation);
        Assert.Equal(PrivateSyncState.Unavailable, result.State);
        Assert.Equal(1, count);
        Assert.DoesNotContain(Access, result.ToString());
        Assert.DoesNotContain(Access, result.Diagnostic);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_timeout_and_headerless_stream_dispose_without_retry(bool caller)
    {
        var clock = new AccountClientClock();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var stream = new PrivateSyncInterruptedStream();
        var count = 0;
        using var http = new HttpClient(new Script(async (_, ct) =>
        {
            count++;
            if (caller) cancel.Cancel();
            else clock.Expire();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        }));
        using var client = new PrivateSyncHttpClient(http, new Uri("https://example.test/root/"), clock);
        var result = await client.BeginResyncAsync(Access, cancel.Token);
        Assert.Equal(caller ? PrivateSyncState.Cancelled : PrivateSyncState.TimedOut, result.State);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Interrupted_body_disposes_stream()
    {
        using var stream = new PrivateSyncInterruptedStream();
        using var http = new HttpClient(new Script((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamContent(stream) })));
        using var client = Client(http);
        Assert.Equal(PrivateSyncState.Unavailable, (await client.MetadataAsync(Access)).State);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task Guards_reject_bad_input_without_network_and_default_headers()
    {
        var count = 0;
        using var http = new HttpClient(new Script((_, _) =>
        {
            count++;
            return Task.FromResult(Json(Metadata));
        }));
        using var client = Client(http);
        Assert.Equal(PrivateSyncState.InvalidRequest, (await client.MetadataAsync("invalid")).State);
        Assert.Equal(PrivateSyncState.InvalidRequest, (await client.MutateAsync(Access, null!)).State);
        Assert.Equal(PrivateSyncState.InvalidRequest, (await client.ChangesAsync(Access, Guid.Empty, 0)).State);
        Assert.Equal(PrivateSyncState.InvalidRequest, (await client.ChangesAsync(Access, Epoch, -1)).State);
        Assert.Equal(PrivateSyncState.InvalidRequest, (await client.ChangesAsync(Access, Epoch, 0, 201)).State);
        Assert.Equal(PrivateSyncState.InvalidRequest, (await client.ReadResyncPageAsync(Access, Manifest, -1)).State);
        Assert.Equal(0, count);
        http.DefaultRequestHeaders.Authorization = new("Bearer", Access);
        Assert.Throws<ArgumentException>(() => new PrivateSyncHttpClient(http, new Uri("https://example.test/")));
        Assert.Equal(PrivateSyncState.InvalidRequest, (await client.MetadataAsync(Access)).State);
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData("http://example.test/")]
    [InlineData("https://user:password@example.test/")]
    [InlineData("https://example.test/?secret=x")]
    [InlineData("https://example.test/#x")]
    [InlineData("/relative")]
    public void Invalid_base_is_rejected_without_echo(string uri)
    {
        using var http = new HttpClient();
        var error = Assert.Throws<ArgumentException>(() => new PrivateSyncHttpClient(http, new Uri(uri, UriKind.RelativeOrAbsolute)));
        Assert.DoesNotContain(uri, error.ToString());
        Assert.DoesNotContain("password", error.ToString());
    }

    [Theory]
    [InlineData("http://localhost:1234/prefix")]
    [InlineData("http://127.0.0.2:1234/prefix")]
    [InlineData("http://[::1]:1234/prefix")]
    [InlineData("https://example.test/prefix")]
    public void Scope_matches_account_server_scope(string url)
    {
        var scope = new AccountServerScope(new Uri(url));
        using var owned = PrivateSyncHttpClient.CreateOwned(new Uri(url));
        Assert.Equal(scope.Key, owned.Scope.Key);
        Assert.Equal(scope.BaseUri, owned.Scope.BaseUri);
    }

    [Fact]
    public async Task Injected_client_survives_disposal()
    {
        using var http = new HttpClient(new Script((_, _) => Task.FromResult(Json(Metadata))));
        new PrivateSyncHttpClient(http, new Uri("https://example.test/")).Dispose();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpResponseMessage Raw(byte[] body, int status = 200)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(body) };
        if (status is >= 200 and < 300 && status != 204)
            response.Content.Headers.ContentType = new("application/json");
        return response;
    }
}

internal sealed class PrivateSyncUnknownLengthContent(byte[] bytes) : HttpContent
{
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
}

internal sealed class PrivateSyncInterruptedStream : MemoryStream
{
    internal bool Disposed;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => throw new IOException("synthetic");
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}
