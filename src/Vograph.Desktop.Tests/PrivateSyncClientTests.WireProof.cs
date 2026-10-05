using System.Net;
using System.Text;
using Vograph.Core.Services.Sync;
using Xunit;
using Zapara.Contracts.Sync;

namespace Vograph.Desktop.Tests;

public sealed partial class PrivateSyncClientTests
{
    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json; charset=utf-16")]
    public async Task Non_json_error_is_invalid_even_when_body_looks_valid(string media)
    {
        using var http = new HttpClient(new Script((_, _) =>
        {
            var response = Json(new SyncError(401, "invalid_session"), 401);
            response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(media);
            return Task.FromResult(response);
        }));
        using var client = Client(http);
        Assert.Equal(PrivateSyncState.InvalidResponse,
            (await client.MetadataAsync(Access, TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task Escaped_unicode_body_is_measured_on_wire_and_owned_request_is_sanitized()
    {
        var value = new FriendValue(null, "Группа", string.Concat(Enumerable.Repeat("\u0001", 3000)), 1, true);
        var mutation = new SyncMutation(Epoch, Op, "friend", Entity, 0, "upsert", value);
        var expected = SyncJson.Serialize(mutation);
        Assert.True(expected.Length > 15000 && expected.Length < SyncValidation.RequestBytes);
        HttpRequestMessage? captured = null;
        byte[]? ownedBytes = null;
        using var http = new HttpClient(new Script(async (request, ct) =>
        {
            captured = request;
            ownedBytes = await request.Content!.ReadAsByteArrayAsync(ct);
            Assert.True(expected.SequenceEqual(ownedBytes));
            Assert.Equal(expected.Length, request.Content.Headers.ContentLength);
            var decoded = SyncJson.Parse<SyncMutation>(ownedBytes);
            Assert.Equal(mutation.OpId, decoded.OpId);
            Assert.Equal(mutation.SyncEpoch, decoded.SyncEpoch);
            return Json(new SyncMutationResult(200, "applied", Metadata,
                new SyncRecord("friend", Entity, 1, false, Now, value)));
        }));
        using var client = Client(http);
        Assert.Equal(PrivateSyncState.Success, (await client.MutateAsync(Access, mutation, TestContext.Current.CancellationToken)).State);
        Assert.NotNull(captured);
        Assert.Empty(captured.Headers);
        Assert.NotNull(ownedBytes);
        // ReadAsByteArrayAsync returns a handler-owned copy, not the client's zeroed backing buffer.
        Assert.Throws<ObjectDisposedException>(() => captured.Content!.ReadAsStream());
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(ownedBytes);
    }

    [Fact]
    public async Task Timeout_budget_spans_headers_and_body_and_disposes_stream()
    {
        var clock = new AccountClientClock();
        using var stream = new TimeoutBody(clock);
        using var http = new HttpClient(new Script((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentType = new("application/json");
            return Task.FromResult(response);
        }));
        using var client = new PrivateSyncHttpClient(http, new Uri("https://example.test/"), clock);
        Assert.Equal(PrivateSyncState.TimedOut, (await client.MetadataAsync(Access, TestContext.Current.CancellationToken)).State);
        Assert.True(stream.Disposed);
    }

    private sealed class TimeoutBody(AccountClientClock clock) : MemoryStream
    {
        public bool Disposed { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            clock.Expire();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return 0;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
