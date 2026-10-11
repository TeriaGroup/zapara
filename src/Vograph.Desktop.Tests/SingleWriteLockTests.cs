using Microsoft.Data.Sqlite;
using Vograph.Core.Services;
using Vograph.Core.Services.Sync;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#161: a single write without a transaction on thread B, while thread A's transaction is open on the shared
/// connection, used to run inside A's transaction and was rolled back with it. It now waits for A and survives.</summary>
public sealed class SingleWriteLockTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vograph-161-" + Guid.NewGuid().ToString("N"));
    private readonly Database _db;

    public SingleWriteLockTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "app.db"));
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE t161(v TEXT NOT NULL)";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    public static TheoryData<string> Kinds => new() { "nonquery", "scalar", "reader" };

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Single_write_survives_a_rollback_of_a_savepoint_open_on_another_thread(string kind)
    {
        var outbox = new PrivateSyncOutbox(_db, enabled: true);
        using var entered = new ManualResetEventSlim();
        var a = Task.Run(() => outbox.InTransaction(() =>
        {
            Insert("rolled back", "nonquery");
            entered.Set();
            Thread.Sleep(300);
            return 0;
        }, () => throw new OperationCanceledException("stale")), TestContext.Current.CancellationToken);
        entered.Wait(TestContext.Current.CancellationToken);
        var b = Task.Run(() => Insert("kept", kind), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<OperationCanceledException>(() => a);
        await b;
        Assert.Equal(["kept"], Rows());
    }

    [Fact]
    public async Task Single_write_survives_a_rollback_of_BeginTransaction_on_another_thread()
    {
        using var entered = new ManualResetEventSlim();
        var a = Task.Run(() =>
        {
            using var write = _db.EnterWrite();
            using var tx = _db.Connection.BeginTransaction();
            Insert("rolled back", "nonquery");
            entered.Set();
            Thread.Sleep(300);
            tx.Rollback();
        }, TestContext.Current.CancellationToken);
        entered.Wait(TestContext.Current.CancellationToken);
        // Created while A's BEGIN is open: before #161 it picked up A's transaction at CreateCommand.
        var b = Task.Run(() => Insert("kept", "nonquery"), TestContext.Current.CancellationToken);
        await a;
        await b;
        Assert.Equal(["kept"], Rows());
    }

    [Fact]
    public async Task Guest_homework_written_during_a_sync_rollback_is_kept()
    {
        // A guest has no outbox: HomeworkService writes with single commands. A signed-in graph's sync transaction on
        // the same connection (here: InTransaction on another thread) must not take the guest write with it.
        var sync = new PrivateSyncOutbox(_db, enabled: true);
        var guest = new HomeworkService(_db, new PrivateSyncOutbox(_db, enabled: false));
        using var entered = new ManualResetEventSlim();
        var a = Task.Run(() => sync.InTransaction(() => { entered.Set(); Thread.Sleep(300); return 0; },
            () => throw new OperationCanceledException("stale")), TestContext.Current.CancellationToken);
        entered.Wait(TestContext.Current.CancellationToken);
        var id = await Task.Run(() => guest.AddHomework("лек ИСТОРИЯ", "гость", 1, new DateTime(2026, 9, 29)), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<OperationCanceledException>(() => a);
        Assert.Equal("гость", guest.GetById(id)?.Text);
    }

    [Fact]
    public void A_transaction_on_the_same_thread_still_sees_its_own_commands()
    {
        using (var write = _db.EnterWrite())
        using (var tx = _db.Connection.BeginTransaction())
        {
            Insert("x", "nonquery"); // created after BEGIN: bound to the open transaction at execution
            tx.Rollback();
        }
        Assert.Empty(Rows());
        Insert("y", "scalar");
        Assert.Equal(["y"], Rows());
    }

    private void Insert(string v, string kind)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = kind == "nonquery" ? "INSERT INTO t161(v) VALUES (@v)" : "INSERT INTO t161(v) VALUES (@v) RETURNING rowid";
        cmd.Parameters.AddWithValue("@v", v);
        switch (kind)
        {
            case "nonquery": cmd.ExecuteNonQuery(); break;
            case "scalar": cmd.ExecuteScalar(); break;
            default: using (var r = cmd.ExecuteReader()) while (r.Read()) { } break;
        }
    }

    private List<string> Rows()
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT v FROM t161 ORDER BY v";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }
}
