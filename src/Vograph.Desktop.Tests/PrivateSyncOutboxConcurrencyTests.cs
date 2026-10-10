using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Vograph.Core.Services;
using Vograph.Core.Services.Sync;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#154: InTransaction from two threads on the shared connection. The second thread's SAVEPOINT used to nest inside
/// the first one's: the outer RELEASE removed it ("no such savepoint") and the outer rollback undid the inner write.</summary>
public sealed class PrivateSyncOutboxConcurrencyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vograph-154-" + Guid.NewGuid().ToString("N"));
    private readonly Database _db;
    private readonly PrivateSyncOutbox _outbox;

    public PrivateSyncOutboxConcurrencyTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "app.db"));
        _outbox = new PrivateSyncOutbox(_db, enabled: true);
        Exec("CREATE TABLE t154(v TEXT NOT NULL)");
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Second_thread_waits_instead_of_nesting_and_its_release_never_fails()
    {
        using var entered = new ManualResetEventSlim();
        var outer = Task.Run(() => _outbox.InTransaction(() =>
        {
            Insert("outer");
            entered.Set();
            Thread.Sleep(150);
            return 0;
        }), TestContext.Current.CancellationToken);
        entered.Wait(TestContext.Current.CancellationToken);
        // Before #154 this SAVEPOINT nested inside the outer one; the outer RELEASE (after 150 ms) removed it while this
        // action still slept, and this RELEASE failed with "no such savepoint".
        var inner = Task.Run(() => _outbox.InTransaction(() =>
        {
            Insert("inner");
            Thread.Sleep(300);
            return 0;
        }), TestContext.Current.CancellationToken);
        await Task.WhenAll(outer, inner);
        Assert.Equal(["inner", "outer"], Rows());
        Exec("BEGIN"); // no transaction is left open: BEGIN would fail inside one
        Exec("ROLLBACK");
    }

    [Fact]
    public async Task Rollback_on_one_thread_does_not_undo_a_write_committed_on_another()
    {
        using var entered = new ManualResetEventSlim();
        var outer = Task.Run(() => _outbox.InTransaction(() =>
        {
            Insert("rolled back");
            entered.Set();
            Thread.Sleep(300);
            return 0;
        }, () => throw new OperationCanceledException("stale")), TestContext.Current.CancellationToken);
        entered.Wait(TestContext.Current.CancellationToken);
        // Before #154: committed inside the outer savepoint and then silently rolled back with it.
        var inner = Task.Run(() => _outbox.InTransaction(() => { Insert("kept"); return 0; }), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<OperationCanceledException>(() => outer);
        await inner;
        Assert.Equal(["kept"], Rows());
    }

    [Fact]
    public async Task Many_threads_commit_and_roll_back_without_losing_or_leaking_rows()
    {
        var errors = new ConcurrentQueue<Exception>();
        var workers = Enumerable.Range(0, 6).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < 150; i++)
            {
                var keep = (i + w) % 3 != 0;
                try
                {
                    _outbox.InTransaction(() => { Insert(keep ? "keep" : "drop"); return 0; },
                        keep ? null : () => throw new InvalidDataException("drop"));
                }
                catch (InvalidDataException) when (!keep) { }
                catch (Exception ex) { errors.Enqueue(ex); }
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        await Task.WhenAll(workers);
        Assert.Empty(errors);
        var rows = Rows();
        Assert.DoesNotContain("drop", rows);
        Assert.Equal(Enumerable.Range(0, 6).Sum(w => Enumerable.Range(0, 150).Count(i => (i + w) % 3 != 0)), rows.Count);
    }

    [Fact]
    public void Nested_InTransaction_on_one_thread_still_works()
    {
        _outbox.InTransaction(() =>
        {
            Insert("a");
            _outbox.InTransaction(() => { Insert("b"); return 0; });
            return 0;
        });
        Assert.Equal(["a", "b"], Rows());
    }

    private void Insert(string v)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO t154(v) VALUES (@v)";
        cmd.Parameters.AddWithValue("@v", v);
        cmd.ExecuteNonQuery();
    }

    private List<string> Rows()
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT v FROM t154 ORDER BY v";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private void Exec(string sql)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
