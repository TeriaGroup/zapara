using System.Net;
using System.Text;
using Vograph.Core.Services.Sync;
using Xunit;
using Zapara.Contracts.Sync;

namespace Vograph.Desktop.Tests;

public sealed partial class PrivateSyncClientTests
{
    [Fact]
    public async Task Foreign_ids_and_status_mismatch_are_invalid_not_success()
    {
        var foreignEpoch = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var foreignEntity = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var cases = new (Func<HttpResponseMessage> Body, Func<PrivateSyncHttpClient, Task<PrivateSyncState>> Call)[]
        {
            (() => Json(new SyncMutationResult(200, "applied", Metadata, new("completion", foreignEntity, 1, false, Now, new CompletionValue(true, Now)))),
                c => State(c.MutateAsync(Access, Mutation))),
            (() => Json(new SyncMutationResult(200, "applied", new(foreignEpoch, 1, 0), Record)),
                c => State(c.MutateAsync(Access, Mutation))),
            (() => Json(new SyncMutationResult(409, "revision_conflict", Metadata, Record), 200),
                c => State(c.MutateAsync(Access, Mutation))),
            (() => Json(new SyncMutationResult(200, "applied", Metadata, Record), 409),
                c => State(c.MutateAsync(Access, Mutation))),
            (() => Json(new SyncChangesPage(new(foreignEpoch, 1, 0), 0, 1, false, [new(1, Op, Record)])),
                c => State(c.ChangesAsync(Access, Epoch, 0, 1))),
            (() => Json(new SyncResyncPage(new(Guid.Parse("22222222-2222-2222-2222-222222222222"), Epoch, 9, Now, Now.AddMinutes(10), 1),
                0, 1, false, [new(1, Record)])),
                c => State(c.ReadResyncPageAsync(Access, Manifest, 0, 1))),
            (() => Json(new SyncError(410, "sync_reset")),
                c => State(c.MetadataAsync(Access))),
        };
        foreach (var item in cases)
        {
            using var http = new HttpClient(new Script((_, _) => Task.FromResult(item.Body())));
            using var client = Client(http);
            Assert.Equal(PrivateSyncState.InvalidResponse, await item.Call(client));
        }
    }

    [Fact]
    public async Task Reset_allows_new_epoch_and_does_not_rewrite_pending_mutation()
    {
        var rotated = new SyncMetadata(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), 0, 0);
        var outcome = new SyncMutationResult(410, "sync_reset", rotated, null);
        using var http = new HttpClient(new Script((_, _) => Task.FromResult(Json(outcome, 410))));
        using var client = Client(http);
        var pending = Mutation;
        var result = await client.MutateAsync(Access, pending);
        Assert.Equal(PrivateSyncState.ResetRequired, result.State);
        Assert.Equal(outcome, result.MutationOutcome);
        Assert.NotEqual(pending.SyncEpoch, result.MutationOutcome!.Metadata.SyncEpoch);
        Assert.Equal(Epoch, pending.SyncEpoch);
        Assert.Equal(Op, pending.OpId);
    }

    [Fact]
    public async Task Feed_cursor_is_last_returned_not_high_water_and_pages_keep_manifest()
    {
        var page = new SyncChangesPage(Metadata, 0, 1, true, [new(1, Op, Record)]);
        var resync = new SyncResyncPage(Manifest, 0, 1, false, [new(1, Record)]);
        using var http = new HttpClient(new Script((r, _) =>
        {
            if (r.RequestUri!.AbsolutePath.Contains("/changes", StringComparison.Ordinal)) return Task.FromResult(Json(page));
            return Task.FromResult(Json(resync));
        }));
        using var client = Client(http);
        var changes = await client.ChangesAsync(Access, Epoch, 0, 1);
        Assert.Equal(PrivateSyncState.Success, changes.State);
        Assert.Equal(1, PrivateSyncHttpClient.PullNextAfterSequence(changes.Value!));
        Assert.NotEqual(changes.Value!.Metadata.CurrentSequence, PrivateSyncHttpClient.PullNextAfterSequence(changes.Value));
        Assert.Equal(1, changes.Value.Changes[0].Sequence);
        var read = await client.ReadResyncPageAsync(Access, Manifest, 0, 1);
        Assert.Equal(Manifest.ManifestId, read.Value!.Manifest.ManifestId);
        Assert.Equal(Manifest.HighWater, read.Value.Manifest.HighWater);
        Assert.Equal(Manifest.SyncEpoch, read.Value.Manifest.SyncEpoch);
    }

    [Fact]
    public async Task Manifest_expired_is_typed_and_problem_json_401_does_not_echo_secrets()
    {
        using var expired = new HttpClient(new Script((_, _) => Task.FromResult(Json(new SyncError(410, "manifest_expired"), 410))));
        using var client = Client(expired);
        var page = await client.ReadResyncPageAsync(Access, Manifest, 0, 1);
        Assert.Equal(PrivateSyncState.ManifestExpired, page.State);
        Assert.Null(page.Value);
        using var auth = new HttpClient(new Script((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(
                    "{\"title\":\"synthetic\",\"status\":401,\"code\":\"invalid_session\"}"))
            };
            response.Content.Headers.ContentType = new("application/problem+json");
            return Task.FromResult(response);
        }));
        using var reauth = Client(auth);
        var result = await reauth.MetadataAsync(Access);
        Assert.Equal(PrivateSyncState.NeedsReauthentication, result.State);
        Assert.DoesNotContain(Access, result.ToString());
        Assert.DoesNotContain(Access, result.Diagnostic);
        Assert.DoesNotContain("synthetic", result.ToString());
    }

    private static async Task<PrivateSyncState> State<T>(Task<PrivateSyncResult<T>> task) where T : class
        => (await task).State;
}
