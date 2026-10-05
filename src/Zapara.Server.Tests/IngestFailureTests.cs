using Npgsql;
using System.Net;
using Xunit;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

public sealed class IngestFailureTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unknown_commit_returns_attempt_without_failure_or_retry()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var input = new TimetableInput(db.Clock);
        Assert.Equal(0, await new IngestService(db.Store, input).IngestFileAsync(PostgresFixture.FixturePath("valid-a.xml"), Ct));
        var before = (await db.Store.ReadCurrentAsync(Ct))!.Meta.SnapshotId;
        await db.ExecuteAsync($"""
            CREATE FUNCTION {db.QuotedSchema}.ingest_delay() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN PERFORM pg_sleep(20); RETURN NEW; END $$;
            CREATE CONSTRAINT TRIGGER ingest_delay AFTER INSERT ON {db.QuotedSchema}.snapshots
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.ingest_delay();
            """, Ct);
        NpgsqlConnection? owned = null;
        var opened = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new SnapshotStore(db.DataSource, db.Schema, db.Clock, () => owned = db.Configuration.CreateDedicatedConnection());
        using var http = new HttpClient(new IngestHandler(async (request, _) =>
        {
            var pid = Assert.IsType<NpgsqlConnection>(owned).ProcessID;
            opened.SetResult(pid);
            Assert.True(await db.ScalarAsync<bool>($"SELECT xact_start IS NULL AND state='idle' FROM pg_stat_activity WHERE pid={pid}", Ct));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(await File.ReadAllBytesAsync(PostgresFixture.FixturePath("valid-b.xml"), Ct))
            };
        }));
        var ingest = new IngestService(store, input).IngestFetchResultAsync(http, Ct);
        try
        {
            var pid = await opened.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            var sleeping = false;
            for (var i = 0; i < 100 && !sleeping; i++)
            {
                sleeping = await db.ScalarAsync<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid={pid} AND wait_event='PgSleep' AND query ILIKE 'COMMIT%')", Ct);
                if (!sleeping) await Task.Delay(20, Ct);
            }
            Assert.True(sleeping);
            Assert.True(await db.ScalarAsync<bool>($"SELECT pg_terminate_backend({pid})", Ct));
            var result = await ingest;
            Assert.Equal(5, result.ExitCode);
            Assert.Equal(FailureCode.PublicationUnknown, result.FailureCode);
            Assert.NotNull(result.AttemptId);
            Assert.Null(result.SnapshotId);
            Assert.Null(result.CleanupFailureCode);
            Assert.Equal("running", await db.ScalarAsync<string>($"SELECT status FROM {db.QuotedSchema}.refresh_attempts WHERE attempt_id='{result.AttemptId}'", Ct));
            Assert.Equal(2L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.refresh_attempts", Ct));
            Assert.Equal(before, (await db.Store.ReadCurrentAsync(Ct))!.Meta.SnapshotId);
            Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.snapshots", Ct));
            output.WriteLine($"UNKNOWN attempt={result.AttemptId} lastgood={before} nativeOwnedPid={pid}");
            await db.ReceiptAsync(Ct);
        }
        finally { await ingest; }
    }

    [Fact]
    public async Task Known_publication_failure_records_failed_preserving_A()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var service = new IngestService(db.Store, new TimetableInput(db.Clock));
        Assert.Equal(0, await service.IngestFileAsync(PostgresFixture.FixturePath("valid-a.xml"), Ct));
        var a = (await db.Store.ReadCurrentAsync(Ct))!.Meta.SnapshotId;
        await db.ExecuteAsync($"""
            CREATE FUNCTION {db.QuotedSchema}.ingest_reject() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN RAISE EXCEPTION 'synthetic-secret-sentinel'; END $$;
            CREATE TRIGGER ingest_reject BEFORE UPDATE ON {db.QuotedSchema}.state
            FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.ingest_reject();
            """, Ct);
        var result = await service.IngestFileResultAsync(PostgresFixture.FixturePath("valid-b.xml"), Ct);
        Assert.Equal(5, result.ExitCode);
        Assert.Equal(FailureCode.DbUnavailable, result.FailureCode);
        Assert.Null(result.CleanupFailureCode);
        var current = (await db.Store.ReadCurrentAsync(Ct))!;
        Assert.Equal(a, current.Meta.SnapshotId);
        Assert.Equal("failed", current.Refresh.LastAttemptStatus);
        Assert.Equal("db_unavailable", current.Refresh.LastFailureCode);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.snapshots", Ct));
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task Broken_lease_cleanup_does_not_erase_source_failure()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        NpgsqlConnection? owned = null;
        var store = new SnapshotStore(db.DataSource, db.Schema, db.Clock, () => owned = db.Configuration.CreateDedicatedConnection());
        using var http = new HttpClient(new IngestHandler(async (request, _) =>
        {
            var pid = Assert.IsType<NpgsqlConnection>(owned).ProcessID;
            Assert.True(await db.ScalarAsync<bool>($"SELECT pg_terminate_backend({pid})", Ct));
            return new HttpResponseMessage(HttpStatusCode.Forbidden) { RequestMessage = request };
        }));
        var result = await new IngestService(store, new TimetableInput(db.Clock)).IngestFetchResultAsync(http, Ct);
        Assert.Equal(2, result.ExitCode);
        Assert.Equal(FailureCode.SourceRejected, result.FailureCode);
        Assert.Equal(FailureCode.DbUnavailable, result.CleanupFailureCode);
        Assert.Equal("running", (await db.Store.ReadSelectionAsync(ct: Ct)).Refresh.LastAttemptStatus);
        await db.ReceiptAsync(Ct);
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 6)]
    public async Task TT016_RunAsync_maps_transport_and_cancellation(bool cancel, int expected)
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var http = new HttpClient(new IngestHandler((_, _) =>
        {
            if (cancel) cancellation.Cancel();
            throw new OperationCanceledException();
        }));
        var service = new IngestService(db.Store, new TimetableInput(db.Clock));
        Assert.Equal(expected, await Zapara.Ingest.Program.RunAsync(["ingest", "--fetch"], db.Store, service, http, cancellation.Token));
        Assert.Equal("failed", (await db.Store.ReadSelectionAsync(ct: Ct)).Refresh.LastAttemptStatus);
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task Failure_cleanup_has_independent_five_second_bound()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await db.ExecuteAsync($"""
            CREATE FUNCTION {db.QuotedSchema}.ingest_delay_failure() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN IF NEW.status='failed' THEN PERFORM pg_sleep(20); END IF; RETURN NEW; END $$;
            CREATE TRIGGER ingest_delay_failure BEFORE UPDATE ON {db.QuotedSchema}.refresh_attempts
            FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.ingest_delay_failure();
            """, Ct);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var http = new HttpClient(new IngestHandler((_, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException();
        }));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var result = await new IngestService(db.Store, new TimetableInput(db.Clock)).IngestFetchResultAsync(http, cancellation.Token);
        Assert.Equal(6, result.ExitCode);
        Assert.Equal(FailureCode.Cancelled, result.FailureCode);
        Assert.NotNull(result.CleanupFailureCode);
        Assert.InRange(timer.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(12));
        Assert.Equal("running", (await db.Store.ReadSelectionAsync(ct: Ct)).Refresh.LastAttemptStatus);
        output.WriteLine($"BOUNDED_CLEANUP elapsedMs={timer.ElapsedMilliseconds} code={result.CleanupFailureCode}");
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task RunAsync_db_init_never_calls_source_and_factory_is_owned()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, initialize: false, ct: Ct);
        var calls = 0;
        using var http = new HttpClient(new IngestHandler((_, _) => { calls++; throw new IOException(); }));
        using var productionHttp = TimetableInput.CreateHttpClient();
        Assert.Equal(Timeout.InfiniteTimeSpan, productionHttp.Timeout);
        var service = new IngestService(db.Store, new TimetableInput(db.Clock));
        Assert.Equal(0, await Zapara.Ingest.Program.RunAsync(["db-init"], db.Store, service, http, Ct));
        Assert.Equal(0, calls);
        Assert.Null((await db.Store.ReadSelectionAsync(ct: Ct)).Refresh.LastAttemptId);
        await db.ReceiptAsync(Ct);
    }
}
