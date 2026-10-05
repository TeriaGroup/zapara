using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Vograph.Core.Services.Sync;
using Xunit;
using Zapara.Contracts.Sync;

namespace Vograph.Desktop.Tests;

public sealed partial class PrivateSyncClientTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("utf8")]
    [InlineData("depth")]
    [InlineData("oversized")]
    [InlineData("media")]
    [InlineData("charset")]
    [InlineData("redirect")]
    [InlineData("status")]
    public async Task Invalid_wire_is_rejected_and_headerless_stream_disposed(string variant)
    {
        var bytes = SyncJson.Serialize(Metadata);
        if (variant == "missing") bytes = "{}"u8.ToArray();
        if (variant == "duplicate") bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("{", "{\"currentSequence\":9,"));
        if (variant == "utf8") bytes = new byte[] { 0xff, 0xfe };
        if (variant == "depth") bytes = Encoding.UTF8.GetBytes(new string('[', 30) + new string(']', 30));
        if (variant == "oversized") bytes = new byte[SyncValidation.PageBytes + 1];
        var stream = new TrackingStream(bytes);
        using var http = new HttpClient(new Script((_, _) =>
        {
            var response = new HttpResponseMessage(variant == "redirect" ? HttpStatusCode.TemporaryRedirect :
                variant == "status" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(variant == "media" ? "text/plain" : "application/json");
            if (variant == "charset") response.Content.Headers.ContentType.CharSet = "utf-16";
            return Task.FromResult(response);
        }));
        using var client = Client(http);
        var result = await client.MetadataAsync(Access);
        Assert.Equal(PrivateSyncState.InvalidResponse, result.State);
        Assert.True(stream.Disposed);
    }

    [Theory]
    [InlineData("entity")]
    [InlineData("epoch")]
    [InlineData("revision")]
    [InlineData("action")]
    [InlineData("http-status")]
    public async Task Mutation_receipt_must_match_request_and_http_status(string variant)
    {
        var record = new SyncRecord("completion", variant == "entity" ? Guid.NewGuid() : Entity,
            variant == "revision" ? 10 : 1, variant == "action", Now,
            variant == "action" ? null : new CompletionValue(true, Now));
        var outcome = new SyncMutationResult(200, "applied", variant == "epoch" ? new SyncMetadata(Guid.NewGuid(), 9, 0) : Metadata, record);
        using var http = new HttpClient(new Script((_, _) => Task.FromResult(Json(outcome, variant == "http-status" ? 409 : 200))));
        using var client = Client(http);
        Assert.Equal(PrivateSyncState.InvalidResponse, (await client.MutateAsync(Access, Mutation)).State);
    }

    [Theory]
    [InlineData("epoch")]
    [InlineData("after")]
    [InlineData("order")]
    [InlineData("next")]
    [InlineData("op")]
    [InlineData("limit")]
    public async Task Feed_rejects_foreign_epoch_wrong_cursor_invalid_order_and_op(string variant)
    {
        var page = new SyncChangesPage(Metadata, 0, 2, true,
            new[] { new SyncChange(1, Op, Record), new SyncChange(2, Op, Record) });
        var text = Encoding.UTF8.GetString(SyncJson.Serialize(page));
        text = variant switch
        {
            "epoch" => text.Replace(Epoch.ToString(), Guid.NewGuid().ToString()),
            "after" => text.Replace("\"afterSequence\":0", "\"afterSequence\":1"),
            "order" => text.Replace("\"sequence\":2", "\"sequence\":1"),
            "next" => text.Replace("\"nextAfterSequence\":2", "\"nextAfterSequence\":9"),
            "op" => text.Replace(Op.ToString(), Guid.Empty.ToString()),
            _ => text
        };
        using var http = new HttpClient(new Script((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(text, Encoding.UTF8, "application/json") })));
        using var client = Client(http);
        Assert.Equal(PrivateSyncState.InvalidResponse, (await client.ChangesAsync(Access, Epoch, 0, variant == "limit" ? 1 : 100)).State);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("epoch")]
    [InlineData("highwater")]
    [InlineData("after")]
    [InlineData("expiry")]
    public async Task Manifest_page_is_pinned_to_full_snapshot(string variant)
    {
        var page = new SyncResyncPage(Manifest, 0, 1, false, new[] { new SyncManifestItem(1, Record) });
        var text = Encoding.UTF8.GetString(SyncJson.Serialize(page));
        text = variant switch
        {
            "id" => text.Replace(Manifest.ManifestId.ToString(), Guid.NewGuid().ToString()),
            "epoch" => text.Replace(Epoch.ToString(), Guid.NewGuid().ToString()),
            "highwater" => text.Replace("\"highWater\":9", "\"highWater\":10"),
            "after" => text.Replace("\"afterOrdinal\":0", "\"afterOrdinal\":1"),
            _ => text.Replace("2026-09-08", "2026-09-09")
        };
        using var http = new HttpClient(new Script((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(text, Encoding.UTF8, "application/json") })));
        using var client = Client(http);
        Assert.Equal(PrivateSyncState.InvalidResponse, (await client.ReadResyncPageAsync(Access, Manifest, 0)).State);
    }

    [Fact]
    public async Task Manifest_expired_is_a_state_not_an_empty_success()
    {
        using var http = new HttpClient(new Script((_, _) => Task.FromResult(Json(new SyncError(410, "manifest_expired"), 410))));
        using var client = Client(http);
        Assert.Equal(PrivateSyncState.ManifestExpired, (await client.ReadResyncPageAsync(Access, Manifest, 0)).State);
    }

    [Fact]
    public async Task Caller_cancellation_is_typed_and_does_not_retry()
    {
        using var cts = new CancellationTokenSource();
        using var http = new HttpClient(new Script(async (_, ct) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return Json(Metadata);
        }));
        using var client = Client(http);
        Assert.Equal(PrivateSyncState.Cancelled, (await client.MetadataAsync(Access, cts.Token)).State);
    }

    [Fact]
    public async Task Invalid_access_and_cursor_do_not_send()
    {
        var calls = 0;
        using var http = new HttpClient(new Script((_, _) => { calls++; return Task.FromResult(Json(Metadata)); }));
        using var client = Client(http);
        Assert.Equal(PrivateSyncState.InvalidRequest, (await client.MetadataAsync("synthetic-invalid\r\n" )).State);
        Assert.Equal(PrivateSyncState.InvalidRequest, (await client.ChangesAsync(Access, Guid.Empty, 0)).State);
        Assert.Equal(PrivateSyncState.InvalidRequest, (await client.ChangesAsync(Access, Epoch, -1)).State);
        Assert.Equal(PrivateSyncState.InvalidRequest, (await client.ChangesAsync(Access, Epoch, 0, 201)).State);
        Assert.Equal(0, calls);
    }

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
