using System.Text;
using Xunit;
using Zapara.Contracts.Sync;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

[Collection("AccountEnvironment")]
public sealed partial class SyncApiTests
{
    private static SyncMutation Put(SyncMetadata metadata, Guid? id = null, long revision = 0, Guid? op = null, string text = "Тест <>& 🌍")
        => new(metadata.SyncEpoch, op ?? Guid.NewGuid(), "homework", id ?? Guid.NewGuid(), revision, "upsert",
            new HomeworkValue("Математика", "математика", text, 1, new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero), null));
    private static Task<long> Count(SyncPostgresFixture db, string table)
        => db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.{table}");

    [Fact]
    public async Task Anonymous_never_initializes_and_fresh_metadata_is_zero()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        await using var host = new SyncApiTestHost(db);
        await host.Send("GET", "/metadata", 401);
        Assert.Equal(0, await Count(db, "sync_state"));
        var session = await Seed(host.Accounts);
        var metadata = await host.Get<SyncMetadata>("/metadata", session.AccessToken);
        Assert.NotEqual(Guid.Empty, metadata.SyncEpoch);
        Assert.Equal(0, metadata.CurrentSequence);
        Assert.Equal(0, metadata.MinAfterSequence);
    }

    [Fact]
    public async Task Receipts_retry_exact_bytes_conflicts_and_tombstone_blocks_resurrection()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        await using var host = new SyncApiTestHost(db);
        var token = (await Seed(host.Accounts)).AccessToken;
        var metadata = await host.Get<SyncMetadata>("/metadata", token);
        var put = Put(metadata);
        var first = await host.Mutate(put, token);
        Assert.Equal(first, await host.Mutate(put, token));
        var stored = await db.Accounts.ScalarAsync<byte[]>($"SELECT body FROM {db.QuotedSchema}.sync_receipts WHERE op_id='{put.OpId}'");
        Assert.Equal(stored, first);
        var reused = SyncJson.Parse<SyncMutationResult>(await host.Mutate(Put(metadata, put.EntityId, op: put.OpId, text: "Другой"), token, 409));
        Assert.Equal("op_id_reused", reused.Code);
        var stale = Put(metadata, put.EntityId);
        var conflict = await host.Mutate(stale, token, 409);
        Assert.Equal("revision_conflict", SyncJson.Parse<SyncMutationResult>(conflict).Code);
        var delete = new SyncMutation(metadata.SyncEpoch, Guid.NewGuid(), "homework", put.EntityId, 1, "delete", null);
        await host.Mutate(delete, token);
        Assert.Equal(conflict, await host.Mutate(stale, token, 409));
        var blocked = SyncJson.Parse<SyncMutationResult>(await host.Mutate(Put(metadata, put.EntityId), token, 409));
        Assert.True(blocked.ServerRecord!.Tombstone);
        Assert.Equal(2, await Count(db, "sync_changes"));
        Assert.Equal(4, await Count(db, "sync_receipts"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"ownerId\":\"private\"}")]
    [InlineData("{\"syncEpoch\":null,\"syncEpoch\":null}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task Malformed_mutations_do_not_initialize(string raw)
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        await using var host = new SyncApiTestHost(db);
        var token = (await Seed(host.Accounts)).AccessToken;
        await host.Send("POST", "/mutations", 400, token, Encoding.UTF8.GetBytes(raw));
        await host.Send("POST", "/mutations", 400, token, new byte[] { 0xff });
        await host.Send("POST", "/mutations", 413, token, new byte[65537]);
        await host.Send("POST", "/resync", 400, token, Encoding.UTF8.GetBytes("{}"));
        Assert.Equal(0, await Count(db, "sync_state"));
        Assert.Equal(0, await Count(db, "sync_receipts"));
    }

    [Theory]
    [InlineData("/metadata?ownerId=x")]
    [InlineData("/changes")]
    [InlineData("/changes?epoch=00000000-0000-0000-0000-000000000000")]
    [InlineData("/changes?epoch=abcdefab-abcd-abcd-abcd-abcdefabcdef&limit=201")]
    [InlineData("/changes?epoch=abcdefab-abcd-abcd-abcd-abcdefabcdef&limit=1&limit=1")]
    [InlineData("/changes?epoch=ABCDEFAB-ABCD-ABCD-ABCD-ABCDEFABCDEF")]
    [InlineData("/changes?epoch=abcdefab-abcd-abcd-abcd-abcdefabcdef&afterSequence=-1")]
    [InlineData("/changes?epoch=abcdefab-abcd-abcd-abcd-abcdefabcdef&afterSequence=9223372036854775808")]
    [InlineData("/resync/not-a-uuid")]
    [InlineData("/resync/abcdefab-abcd-abcd-abcd-abcdefabcdef?afterOrdinal=-1")]
    public async Task Strict_queries_fail_before_sync_state(string path)
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        await using var host = new SyncApiTestHost(db);
        var token = (await Seed(host.Accounts)).AccessToken;
        Assert.Equal("invalid_request", SyncJson.Parse<SyncError>(await host.Send("GET", path, 400, token)).Code);
        Assert.Equal(0, await Count(db, "sync_state"));
    }
}
