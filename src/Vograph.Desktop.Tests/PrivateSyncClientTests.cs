using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Vograph.Core.Services.Sync;
using Xunit;
using Zapara.Contracts.Sync;

namespace Vograph.Desktop.Tests;

public sealed partial class PrivateSyncClientTests
{
    private static readonly Guid Epoch = Guid.NewGuid();
    private static readonly Guid Entity = Guid.NewGuid();
    private static readonly Guid Op = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Access = "za_" + new string('A', 43);
    private static SyncMetadata Metadata => new(Epoch, 9, 0);
    private static SyncMutation Mutation => new(Epoch, Op, "completion", Entity, 0, "upsert", new CompletionValue(true, Now));
    private static SyncRecord Record => new("completion", Entity, 1, false, Now, new CompletionValue(true, Now));
    private static SyncResyncManifest Manifest => new(Guid.Parse("11111111-1111-1111-1111-111111111111"), Epoch, 9, Now, Now.AddMinutes(10), 1);
    private static HttpResponseMessage Json<T>(T value, int status = 200)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(SyncJson.Serialize(value)) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }
    private sealed class Script(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => action(request, cancellationToken);
    }
    private static PrivateSyncHttpClient Client(HttpClient http) => new(http, new Uri("https://example.test/root/"));

    [Fact]
    public async Task Five_routes_use_exact_prefix_query_headers_and_canonical_explicit_retry()
    {
        var paths = new List<string>();
        var bodies = new List<byte[]>();
        using var http = new HttpClient(new Script(async (r, ct) =>
        {
            Assert.Equal("Bearer", r.Headers.Authorization?.Scheme);
            Assert.True(r.Headers.Authorization?.Parameter == Access);
            Assert.Contains(r.Headers.Accept, h => h.MediaType == "application/json");
            Assert.Equal("https://example.test", r.RequestUri!.GetLeftPart(UriPartial.Authority));
            paths.Add(r.Method + " " + r.RequestUri.PathAndQuery);
            if (r.RequestUri.AbsolutePath.EndsWith("mutations"))
            {
                var bytes = await r.Content!.ReadAsByteArrayAsync(ct);
                Assert.True(bytes.SequenceEqual(SyncJson.Serialize(Mutation)));
                bodies.Add(bytes);
                return Json(new SyncMutationResult(200, "applied", Metadata, Record));
            }
            if (r.RequestUri.AbsolutePath.EndsWith("metadata")) return Json(Metadata);
            if (r.RequestUri.AbsolutePath.EndsWith("changes"))
                return Json(new SyncChangesPage(Metadata, 0, 1, true, new[] { new SyncChange(1, Op, Record) }));
            if (r.Method == HttpMethod.Post)
            {
                Assert.True(r.Content is null || (await r.Content.ReadAsByteArrayAsync(ct)).Length == 0);
                return Json(Manifest);
            }
            return Json(new SyncResyncPage(Manifest, 0, 1, false, new[] { new SyncManifestItem(1, Record) }));
        }));
        using var client = Client(http);
        Assert.Equal(PrivateSyncState.Success, (await client.MetadataAsync(Access)).State);
        Assert.Equal(PrivateSyncState.Success, (await client.MutateAsync(Access, Mutation)).State);
        Assert.Equal(PrivateSyncState.Success, (await client.MutateAsync(Access, Mutation)).State);
        var changes = await client.ChangesAsync(Access, Epoch, 0, 1);
        Assert.Equal(PrivateSyncState.Success, changes.State);
        Assert.Equal(1, changes.Value!.NextAfterSequence);
        Assert.Equal(9, changes.Value.Metadata.CurrentSequence);
        Assert.Equal(PrivateSyncState.Success, (await client.BeginResyncAsync(Access)).State);
        Assert.Equal(PrivateSyncState.Success, (await client.ReadResyncPageAsync(Access, Manifest, 0, 1)).State);
        Assert.Equal(new[] { "GET /root/api/v1/sync/metadata", "POST /root/api/v1/sync/mutations", "POST /root/api/v1/sync/mutations",
            $"GET /root/api/v1/sync/changes?epoch={Epoch:D}&afterSequence=0&limit=1", "POST /root/api/v1/sync/resync",
            $"GET /root/api/v1/sync/resync/{Manifest.ManifestId:D}?afterOrdinal=0&limit=1" }, paths);
        Assert.True(bodies[0].SequenceEqual(bodies[1]));
        Assert.Empty(http.DefaultRequestHeaders);
    }

    [Theory]
    [InlineData(409, "revision_conflict", PrivateSyncState.Conflict)]
    [InlineData(409, "op_id_reused", PrivateSyncState.Conflict)]
    [InlineData(410, "sync_reset", PrivateSyncState.ResetRequired)]
    public async Task Mutation_outcomes_preserve_receipt_and_allow_new_reset_epoch(int status, string code, PrivateSyncState state)
    {
        var outcome = new SyncMutationResult(status, code, status == 410 ? new SyncMetadata(Guid.NewGuid(), 9, 0) : Metadata,
            code == "revision_conflict" ? Record : null);
        using var http = new HttpClient(new Script((_, _) => Task.FromResult(Json(outcome, status))));
        using var client = Client(http);
        var result = await client.MutateAsync(Access, Mutation);
        Assert.Equal(state, result.State);
        Assert.Equal(outcome, result.MutationOutcome);
    }

    [Theory]
    [InlineData(410, "sync_reset", PrivateSyncState.ResetRequired)]
    [InlineData(401, "invalid_session", PrivateSyncState.NeedsReauthentication)]
    [InlineData(429, "rate_limited", PrivateSyncState.RateLimited)]
    [InlineData(503, "db_unavailable", PrivateSyncState.Unavailable)]
    public async Task Errors_are_typed_and_never_automatically_retried(int status, string code, PrivateSyncState state)
    {
        var count = 0;
        using var http = new HttpClient(new Script((_, _) =>
        {
            count++;
            var r = Json(new SyncError(status, code), status);
            r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromDays(7));
            return Task.FromResult(r);
        }));
        using var client = Client(http);
        var result = await client.ChangesAsync(Access, Epoch, 0);
        Assert.Equal(state, result.State);
        Assert.Equal(1, count);
        if (status == 429) Assert.Equal(TimeSpan.FromMinutes(5), result.RetryAfter);
    }
}
