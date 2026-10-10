using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.Sqlite;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#130: closing the shared SQLite connection while other threads still create, run and dispose commands
/// crashed inside SqliteConnection.Close() and hung test runs. The connection now waits for commands in flight.</summary>
public class DatabaseShutdownTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vograph-130-" + Guid.NewGuid().ToString("N"));

    public DatabaseShutdownTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private GuardedSqliteConnection Open(string name = "db")
    {
        var connection = new GuardedSqliteConnection($"Data Source={Path.Combine(_dir, name + ".sqlite")};Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS t(id INTEGER PRIMARY KEY, v TEXT)";
        cmd.ExecuteNonQuery();
        return connection;
    }

    [Fact]
    public async Task Shutdown_while_other_threads_use_the_connection_never_crashes_close()
    {
        for (var round = 0; round < 40; round++)
        {
            var connection = Open("stress" + round);
            var errors = new ConcurrentQueue<Exception>();
            using var go = new ManualResetEventSlim();
            var workers = Enumerable.Range(0, 6).Select(w => Task.Run(() =>
            {
                go.Wait();
                for (var i = 0; i < 400; i++)
                {
                    try
                    {
                        using var cmd = connection.CreateCommand();
                        cmd.CommandText = "SELECT count(*) FROM t";
                        cmd.ExecuteScalar();
                    }
                    catch (ObjectDisposedException) { return; } // the database is going away: refused, not crashed
                    catch (Exception ex) { errors.Enqueue(ex); return; }
                }
            }, TestContext.Current.CancellationToken)).ToArray();
            go.Set();
            await Task.Delay(round % 5, TestContext.Current.CancellationToken);
            var closed = connection.Shutdown(TimeSpan.FromSeconds(10));
            await Task.WhenAll(workers);
            Assert.True(closed, $"round {round}: shutdown did not finish");
            Assert.Equal(ConnectionState.Closed, connection.State);
            Assert.Equal(0, connection.InFlight);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public async Task Shutdown_waits_for_a_command_in_flight_and_refuses_new_ones()
    {
        var connection = Open();
        var running = connection.CreateCommand();
        running.CommandText = "SELECT 1";
        var shutdown = Task.Run(() => connection.Shutdown(TimeSpan.FromSeconds(10)), TestContext.Current.CancellationToken);
        await Waits.Until(() => connection.IsShuttingDown, "shutdown started");
        Assert.Throws<ObjectDisposedException>(() => connection.CreateCommand());
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.False(shutdown.IsCompleted, "the connection must not close under a running command");
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal(1L, running.ExecuteScalar());
        running.Dispose();
        Assert.True(await shutdown);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void A_command_that_outlives_the_wait_closes_the_connection_when_it_is_disposed()
    {
        var connection = Open();
        var running = connection.CreateCommand();
        Assert.False(connection.Shutdown(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Throws<ObjectDisposedException>(() => connection.CreateCommand());
        running.Dispose();
        Assert.Equal(ConnectionState.Closed, connection.State);
        connection.Dispose(); // a second close is a no-op
    }

    [Fact]
    public void An_open_transaction_is_rolled_back_on_shutdown()
    {
        var connection = Open();
        var tx = connection.BeginTransaction();
        using (var insert = connection.CreateCommand()) { insert.CommandText = "INSERT INTO t(v) VALUES ('lost')"; insert.ExecuteNonQuery(); }
        Assert.True(connection.Shutdown(TimeSpan.FromSeconds(1)));
        tx.Dispose();
        using var check = new SqliteConnection($"Data Source={Path.Combine(_dir, "db.sqlite")};Pooling=False");
        check.Open();
        using var count = check.CreateCommand();
        count.CommandText = "SELECT count(*) FROM t";
        Assert.Equal(0L, count.ExecuteScalar());
    }

    [Fact]
    public void Database_dispose_goes_through_the_guarded_shutdown()
    {
        var db = new Database(Path.Combine(_dir, "app.db"));
        var connection = Assert.IsType<GuardedSqliteConnection>(db.Connection);
        var running = connection.CreateCommand();
        Assert.False(db.Shutdown(TimeSpan.FromMilliseconds(50)));
        running.Dispose();
        Assert.Equal(ConnectionState.Closed, connection.State);
        db.Dispose();
        SqliteConnection.ClearPool(connection);
    }
}
