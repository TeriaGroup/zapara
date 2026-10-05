using System.Text.Json;
using Xunit;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

public sealed partial class PublicationTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> PublishAsync(PostgresFixture db, bool smaller = false)
    {
        await using var lease = await db.Store.TryAcquireAsync(Ct);
        Assert.NotNull(lease);
        return await db.Store.PublishAsync(lease, TestSnapshotFactory.Create(db.Clock, smaller), Ct);
    }

    [Theory]
    [InlineData(SourceKind.File)]
    [InlineData(SourceKind.Http)]
    public async Task TT005_Store_atomic_success(SourceKind kind)
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        Assert.Null(await db.Store.ReadCurrentAsync(Ct));
        var snapshot = TestSnapshotFactory.Create(db.Clock, kind: kind);
        await using var lease = await db.Store.TryAcquireAsync(Ct);
        Assert.NotNull(lease);
        var id = await db.Store.PublishAsync(lease, snapshot, Ct);
        Assert.NotEqual(Guid.Empty, id);
        var read = Assert.IsType<SnapshotRead>(await db.Store.ReadCurrentAsync(Ct));
        Assert.Equal(id, read.Meta.SnapshotId);
        Assert.Equal(snapshot.Groups, read.Payload.Groups);
        Assert.Equal(snapshot.Lessons, read.Payload.Lessons);
        Assert.Equal(snapshot.Source.SourceSha256, read.Meta.SourceSha256);
        Assert.Equal(snapshot.Source.SourceUrl, read.Meta.SourceUrl);
        Assert.Equal(snapshot.Source.SourceModifiedAt, read.Meta.SourceModifiedAt);
        Assert.Equal(snapshot.Source.FetchedAtUtc, read.Meta.FetchedAt);
        Assert.Equal(db.Clock.Now, read.Meta.PublishedAt);
        Assert.Equal(TimeSpan.Zero, read.Meta.PublishedAt.Offset);
        Assert.False(read.Meta.Stale);
        Assert.Equal("success", read.Refresh.LastAttemptStatus);
        Assert.Equal(lease.AttemptId, read.Refresh.LastAttemptId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.PublishAsync(lease, snapshot, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.RecordFailedAttemptAsync(lease, FailureCode.DbUnavailable, Ct));
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.snapshots", Ct));
        Assert.Equal(snapshot.Source.Bytes.ToArray(), await db.ScalarAsync<byte[]>($"SELECT original_xml FROM {db.QuotedSchema}.snapshots", Ct));
        using var json = JsonDocument.Parse(await db.ScalarAsync<string>($"SELECT payload::text FROM {db.QuotedSchema}.snapshots", Ct));
        Assert.Equal(new[] { "groups", "lessons", "period" }, json.RootElement.EnumerateObject().Select(p => p.Name).Order());
        output.WriteLine($"READ {db.Schema} snapshot={id} groups={read.Payload.Groups.Length} lessons={read.Payload.Lessons.Length} kind={read.Meta.SourceKind} sha256={read.Meta.SourceSha256} stale={read.Meta.Stale}");
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task TT007_Store_lock_and_rollback()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var id = await PublishAsync(db);
        var before = (await db.Store.ReadCurrentAsync(Ct))!;
        await using (var lease = await db.Store.TryAcquireAsync(Ct))
        {
            Assert.NotNull(lease);
            Assert.Null(await db.NewStore().TryAcquireAsync(Ct));
            Assert.Equal(2, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.refresh_attempts", Ct));
            await db.ExecuteAsync($"""
                CREATE FUNCTION {db.QuotedSchema}.reject_pointer() RETURNS trigger LANGUAGE plpgsql AS
                $$ BEGIN RAISE EXCEPTION 'owned test rollback'; END $$;
                CREATE TRIGGER reject_pointer BEFORE UPDATE ON {db.QuotedSchema}.state
                FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.reject_pointer();
                """, Ct);
            await Assert.ThrowsAnyAsync<Exception>(() => db.Store.PublishAsync(lease, TestSnapshotFactory.Create(db.Clock, true), Ct));
            var after = (await db.Store.ReadCurrentAsync(Ct))!;
            Assert.Equal(id, after.Meta.SnapshotId);
            Assert.Equal(before.Payload.Groups, after.Payload.Groups);
            Assert.Equal(before.Payload.Lessons, after.Payload.Lessons);
            Assert.Equal(before.Meta.SourceSha256, after.Meta.SourceSha256);
            Assert.Equal(TestSnapshotFactory.Create(db.Clock).Source.Bytes.ToArray(),
                await db.ScalarAsync<byte[]>($"SELECT original_xml FROM {db.QuotedSchema}.snapshots", Ct));
            Assert.Equal("running", after.Refresh.LastAttemptStatus);
            Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.snapshots", Ct));
            await db.Store.RecordFailedAttemptAsync(lease, FailureCode.DbUnavailable, Ct);
            output.WriteLine($"ROLLBACK {db.Schema} before={id} after={after.Meta.SnapshotId} sha256={after.Meta.SourceSha256} groups={after.Payload.Groups.Length}");
            await db.ReceiptAsync(Ct);
        }
        await using (var pending = await db.Store.TryAcquireAsync(Ct)) { Assert.NotNull(pending); }
        Assert.Equal("running", (await db.Store.ReadCurrentAsync(Ct))!.Refresh.LastAttemptStatus);
        await using var recovery = await db.NewStore().TryAcquireAsync(Ct);
        Assert.NotNull(recovery);
        var recovered = (await db.Store.ReadCurrentAsync(Ct))!;
        Assert.Equal(id, recovered.Meta.SnapshotId);
        Assert.True(recovered.Refresh.Abandoned);
        Assert.True(recovered.Meta.Stale);
        Assert.Equal("abandoned", recovered.Refresh.LastFailureCode);
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task TT008_Store_smaller_and_pinned()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var a = await PublishAsync(db);
        var b = await PublishAsync(db, true);
        Assert.NotEqual(a, b);
        var current = (await db.Store.ReadCurrentAsync(Ct))!;
        Assert.Equal(b, current.Meta.SnapshotId);
        Assert.Single(current.Payload.Groups);
        Assert.Equal("пр Физика", Assert.Single(current.Payload.Lessons).Value.SubjectRaw);
        var pinned = (await db.Store.ReadPinnedAsync(a, Ct))!;
        Assert.Contains(pinned.Payload.Groups, g => g.Id == "9999" && g.LessonCount == 0);
        Assert.True(pinned.Meta.Stale);
        Assert.False((await db.Store.ReadPinnedAsync(b, Ct))!.Meta.Stale);
        var missing = await db.Store.ReadSelectionAsync(Guid.NewGuid(), Ct);
        Assert.Equal(b, missing.CurrentId);
        Assert.Null(missing.Selected);
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task TT009_Store_reopen()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await PublishAsync(db);
        var b = await PublishAsync(db, true);
        await db.DataSource.DisposeAsync();
        await using var reopened = db.Configuration.CreateDataSource();
        var store = new SnapshotStore(reopened, db.Schema, db.Clock, db.Configuration.CreateDedicatedConnection);
        await store.EnsureSchemaAsync(Ct);
        var read = Assert.IsType<SnapshotRead>(await store.ReadCurrentAsync(Ct));
        Assert.Equal(b, read.Meta.SnapshotId);
        Assert.Single(read.Payload.Groups);
        output.WriteLine($"REOPEN {db.Schema} current={b} groups={read.Payload.Groups.Length}");
        await db.ReceiptAsync(Ct);
    }
}
