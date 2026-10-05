using Npgsql;
using Xunit;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

public sealed partial class PublicationTests
{
    [Fact]
    public async Task TT005_Store_baseline_idempotent_and_empty_selection()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await Task.WhenAll(db.Store.EnsureSchemaAsync(Ct), db.NewStore().EnsureSchemaAsync(Ct));
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.state WHERE singleton", Ct));
        var selection = await db.Store.ReadSelectionAsync(Guid.NewGuid(), Ct);
        Assert.Null(selection.CurrentId);
        Assert.Null(selection.Selected);
        Assert.Null(selection.Refresh.LastAttemptId);
        await db.ReceiptAsync(Ct);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("version")]
    [InlineData("structure")]
    [InlineData("missing")]
    public async Task TT005_Store_rejects_schema_without_mutation(string scenario)
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, initialize: false, ct: Ct);
        if (scenario == "foreign") await db.ExecuteAsync($"CREATE TABLE {db.QuotedSchema}.foreign_data(value int); INSERT INTO {db.QuotedSchema}.foreign_data VALUES(42)", Ct);
        if (scenario == "version") await db.ExecuteAsync($"CREATE TABLE {db.QuotedSchema}.schema_version(version int); INSERT INTO {db.QuotedSchema}.schema_version VALUES(2)", Ct);
        if (scenario == "structure")
        {
            await db.Store.EnsureSchemaAsync(Ct);
            await db.ExecuteAsync($"ALTER TABLE {db.QuotedSchema}.snapshots DROP COLUMN source_sha256", Ct);
        }
        if (scenario == "missing")
        {
            var missing = new SnapshotStore(db.DataSource, db.Schema + "_missing", db.Clock, db.Configuration.CreateDedicatedConnection);
            await Assert.ThrowsAnyAsync<Exception>(() => missing.EnsureSchemaAsync(Ct));
            Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{db.Schema}_missing'", Ct));
            return;
        }
        var before = await db.ScalarAsync<long>($"SELECT count(*) FROM pg_class WHERE relnamespace='{db.Schema}'::regnamespace", Ct);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Store.EnsureSchemaAsync(Ct));
        Assert.Equal(before, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_class WHERE relnamespace='{db.Schema}'::regnamespace", Ct));
        if (scenario == "foreign") Assert.Equal(42, await db.ScalarAsync<int>($"SELECT value FROM {db.QuotedSchema}.foreign_data", Ct));
        if (scenario == "version") Assert.Equal(2, await db.ScalarAsync<int>($"SELECT version FROM {db.QuotedSchema}.schema_version", Ct));
    }

    [Fact]
    public async Task TT007_Store_lease_scope_terminal_cancellation_and_independent_schemas()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await using var other = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await using var lease = await db.Store.TryAcquireAsync(Ct);
        Assert.NotNull(lease);
        await using var independent = await other.Store.TryAcquireAsync(Ct);
        Assert.NotNull(independent);
        var snapshot = TestSnapshotFactory.Create(db.Clock);
        await Assert.ThrowsAnyAsync<Exception>(() => db.NewStore().PublishAsync(lease, snapshot, Ct));
        await Assert.ThrowsAnyAsync<Exception>(() => other.Store.RecordFailedAttemptAsync(lease, FailureCode.Cancelled, Ct));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Store.PublishAsync(lease, snapshot, cancelled.Token));
        await db.Store.RecordFailedAttemptAsync(lease, FailureCode.Cancelled, Ct);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Store.PublishAsync(lease, snapshot, Ct));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Store.RecordFailedAttemptAsync(lease, FailureCode.DbUnavailable, Ct));
        await lease.DisposeAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => db.Store.PublishAsync(lease, snapshot, Ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Store.TryAcquireAsync(cancelled.Token));
        await using var next = await db.Store.TryAcquireAsync(Ct);
        Assert.NotNull(next);
        await db.ReceiptAsync(Ct);
        await other.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task TT007_Store_connection_loss_releases_owned_lock()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var id = await PublishAsync(db);
        NpgsqlConnection? ownedConnection = null;
        var store = new SnapshotStore(db.DataSource, db.Schema, db.Clock, () => ownedConnection = db.Configuration.CreateDedicatedConnection());
        await using var lease = await store.TryAcquireAsync(Ct);
        Assert.NotNull(lease);
        Assert.NotNull(ownedConnection);
        var pid = ownedConnection.ProcessID;
        Assert.False(new NpgsqlConnectionStringBuilder(ownedConnection.ConnectionString).Pooling);
        Assert.True(await db.ScalarAsync<bool>($"SELECT state='idle' AND xact_start IS NULL FROM pg_stat_activity WHERE pid={pid}", Ct));
        Assert.True(await db.ScalarAsync<bool>($"SELECT pg_terminate_backend({pid})", Ct));
        await Assert.ThrowsAnyAsync<Exception>(() => store.PublishAsync(lease, TestSnapshotFactory.Create(db.Clock, true), Ct));
        await lease.DisposeAsync();
        await using var recovered = await db.Store.TryAcquireAsync(Ct);
        Assert.NotNull(recovered);
        var read = (await db.Store.ReadCurrentAsync(Ct))!;
        Assert.Equal(id, read.Meta.SnapshotId);
        Assert.Equal("abandoned", read.Refresh.LastFailureCode);
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task TT008_Store_stale_sequence_history_and_age_boundary()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await PublishAsync(db);
        var fetched = db.Clock.Now;
        db.Clock.Now = fetched.AddHours(24).AddTicks(-1);
        Assert.False((await db.Store.ReadCurrentAsync(Ct))!.Meta.Stale);
        db.Clock.Now = fetched.AddHours(24);
        Assert.True((await db.Store.ReadCurrentAsync(Ct))!.Meta.Stale);
        db.Clock.Now = fetched;
        await using (var failed = await db.Store.TryAcquireAsync(Ct))
        {
            Assert.NotNull(failed);
            await db.Store.RecordFailedAttemptAsync(failed, FailureCode.SnapshotMalformed, Ct);
        }
        Assert.True((await db.Store.ReadCurrentAsync(Ct))!.Meta.Stale);
        await PublishAsync(db, true);
        var fresh = (await db.Store.ReadCurrentAsync(Ct))!;
        Assert.False(fresh.Meta.Stale);
        Assert.Equal("snapshot_malformed", fresh.Refresh.LastFailureCode);
        Assert.False(fresh.Refresh.Abandoned);
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task TT005_Store_rejects_inconsistent_identity_and_empty_source()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await using var lease = await db.Store.TryAcquireAsync(Ct);
        Assert.NotNull(lease);
        var a = TestSnapshotFactory.Create(db.Clock);
        var invalid = new ValidatedSnapshot(a.Period, [a.Groups[0], a.Groups[0]], a.Lessons, a.Source);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Store.PublishAsync(lease, invalid, Ct));
        var empty = new ValidatedSnapshot(a.Period, a.Groups, a.Lessons, SourceDocument.Create([], SourceKind.File, db.Clock));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Store.PublishAsync(lease, empty, Ct));
        Assert.Null(await db.Store.ReadCurrentAsync(Ct));
        Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.snapshots", Ct));
    }
}
