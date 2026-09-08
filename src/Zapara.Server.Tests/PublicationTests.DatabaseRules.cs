using Npgsql;
using Xunit;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

public sealed partial class PublicationTests
{
    [Fact]
    public async Task TT007_Store_commit_transport_loss_is_unknown_and_not_retryable()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var a = await PublishAsync(db);
        await db.ExecuteAsync($"""
            CREATE FUNCTION {db.QuotedSchema}.delay_commit() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN PERFORM pg_sleep(20); RETURN NEW; END $$;
            CREATE CONSTRAINT TRIGGER delay_commit AFTER INSERT ON {db.QuotedSchema}.snapshots
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.delay_commit();
            """, Ct);
        NpgsqlConnection? owned = null;
        var store = new SnapshotStore(db.DataSource, db.Schema, db.Clock, () => owned = db.Configuration.CreateDedicatedConnection());
        await using var lease = await store.TryAcquireAsync(Ct);
        Assert.NotNull(lease);
        Assert.NotNull(owned);
        var pid = owned.ProcessID;
        var publish = store.PublishAsync(lease, TestSnapshotFactory.Create(db.Clock, true), Ct);
        try
        {
            var sleeping = false;
            for (var i = 0; i < 100 && !sleeping; i++)
            {
                sleeping = await db.ScalarAsync<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid={pid} AND wait_event='PgSleep' AND query ILIKE 'COMMIT%')", Ct);
                if (!sleeping) await Task.Delay(20, Ct);
            }
            Assert.True(sleeping, "Owned backend must be executing deferred COMMIT trigger.");
            Assert.True(await db.ScalarAsync<bool>($"SELECT pg_terminate_backend({pid})", Ct));
            var error = await Assert.ThrowsAsync<StoreException>(() => publish);
            Assert.Equal(FailureCode.PublicationUnknown, error.FailureCode);
            Assert.Equal(lease.AttemptId, error.AttemptId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordFailedAttemptAsync(lease, FailureCode.DbUnavailable, Ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.PublishAsync(lease, TestSnapshotFactory.Create(db.Clock), Ct));
        }
        finally
        {
            await lease.DisposeAsync();
            try { await publish; } catch (StoreException) { }
        }
        Assert.Equal(a, (await db.Store.ReadCurrentAsync(Ct))!.Meta.SnapshotId);
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.snapshots", Ct));
        await using var next = await db.Store.TryAcquireAsync(Ct);
        Assert.NotNull(next);
        Assert.Equal("abandoned", (await db.Store.ReadCurrentAsync(Ct))!.Refresh.LastFailureCode);
        await db.ReceiptAsync(Ct);
    }

    [Theory]
    [InlineData("status=NULL")]
    [InlineData("status='unknown'")]
    [InlineData("error_code='secret'")]
    [InlineData("status='success'")]
    [InlineData("status='failed',finished_at=now()")]
    [InlineData("status='abandoned',finished_at=now(),error_code='cancelled'")]
    public async Task TT005_Store_attempt_sql_constraints(string assignment)
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await using var lease = await db.Store.TryAcquireAsync(Ct);
        Assert.NotNull(lease);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync($"UPDATE {db.QuotedSchema}.refresh_attempts SET {assignment}", Ct));
        Assert.Contains(error.SqlState, new[] { "23514", "23502" });
        Assert.Equal("running", (await db.Store.ReadSelectionAsync(ct: Ct)).Refresh.LastAttemptStatus);
        await db.ReceiptAsync(Ct);
    }

    [Theory]
    [InlineData("payload", "'[]'::jsonb")]
    [InlineData("original_xml", "''::bytea")]
    [InlineData("original_xml", "decode(repeat('00',16777217),'hex')")]
    [InlineData("source_sha256", "repeat('A',64)")]
    [InlineData("source_kind", "'unknown'")]
    [InlineData("source_url", "'https://unapproved.invalid/'")]
    [InlineData("source_modified_at", "now()")]
    public async Task TT005_Store_snapshot_sql_constraints(string column, string expression)
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var a = await PublishAsync(db);
        await using var lease = await db.Store.TryAcquireAsync(Ct);
        Assert.NotNull(lease);
        var columns = new[] { "payload", "original_xml", "source_kind", "source_url", "source_sha256", "fetched_at", "published_at", "source_modified_at" };
        var values = columns.Select(name => name == column ? expression : name);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync($"""
            INSERT INTO {db.QuotedSchema}.snapshots(snapshot_id,attempt_id,{string.Join(',', columns)})
            SELECT '{Guid.NewGuid()}'::uuid,'{lease.AttemptId}'::uuid,{string.Join(',', values)} FROM {db.QuotedSchema}.snapshots
            """, Ct));
        Assert.Equal("23514", error.SqlState);
        Assert.Equal(a, (await db.Store.ReadCurrentAsync(Ct))!.Meta.SnapshotId);
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.snapshots", Ct));
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task TT005_Store_baseline_rejects_weakened_named_check()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        output.WriteLine(await db.ScalarAsync<string>($"SELECT json_agg(json_build_object('name',conname,'definition',pg_get_constraintdef(oid)) ORDER BY conname)::text FROM pg_constraint WHERE connamespace='{db.Schema}'::regnamespace", Ct));
        await db.ExecuteAsync($"ALTER TABLE {db.QuotedSchema}.snapshots DROP CONSTRAINT snapshot_bytes; ALTER TABLE {db.QuotedSchema}.snapshots ADD CONSTRAINT snapshot_bytes CHECK(true)", Ct);
        await Assert.ThrowsAsync<StoreException>(() => db.Store.EnsureSchemaAsync(Ct));
        Assert.Equal("CHECK (true)", await db.ScalarAsync<string>($"SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE connamespace='{db.Schema}'::regnamespace AND conname='snapshot_bytes'", Ct));
    }
}
