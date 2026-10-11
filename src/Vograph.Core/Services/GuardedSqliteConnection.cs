using System.Data;
using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Vograph.Core.Services;

/// <summary>
/// #130: the one SqliteConnection behind <see cref="Database"/> is reached from the UI thread, background refreshes,
/// sync and notifications. Microsoft.Data.Sqlite keeps the connection's live commands in a plain List that
/// CreateCommand adds to, SqliteCommand.Dispose removes from and Close enumerates — none of it synchronised. A command
/// created or disposed on another thread while the database was closing crashed Close() ("Collection was modified",
/// index out of range, NullReferenceException) and could leave the process stuck at shutdown.
/// This connection serialises those list changes, counts commands in flight (created, not yet disposed) and closes
/// only when none is left: <see cref="Shutdown"/> stops new commands at once, waits for the running ones and, if one
/// outlives the wait, hands the close to the last command's Dispose instead of closing under it.
/// </summary>
public sealed class GuardedSqliteConnection : SqliteConnection
{
    private readonly object _sync = new();
    private int _inFlight;
    private bool _closing;
    private bool _closed;
    private volatile bool _inClose; // Close() rolls an open transaction back with its own ROLLBACK command
    private readonly object _write = new();

    public GuardedSqliteConnection(string connectionString) : base(connectionString) { }

    /// <summary>#154/#161: the connection has one transaction and one savepoint stack for every thread. Whatever runs
    /// while another thread's transaction is open becomes part of it: a nested SAVEPOINT loses its RELEASE (#154), a
    /// single INSERT/UPDATE/DELETE is rolled back with the other thread's transaction (#161). So every command
    /// execution (<see cref="GuardedCommand"/>) and every multi-statement transaction (Database.EnterWrite around
    /// InTransaction and BeginTransaction) holds this one lock. Reentrant (Monitor), uncontended in the normal case.
    /// Lock order: CoreGate, then this lock, then the internal command-list lock. Never await while holding it.</summary>
    public WriteScope EnterWrite()
    {
        Monitor.Enter(_write);
        return new WriteScope(_write);
    }

    public readonly struct WriteScope : IDisposable
    {
        private readonly object? _gate;
        internal WriteScope(object gate) => _gate = gate;
        public void Dispose() { if (_gate is not null) Monitor.Exit(_gate); }
    }

    /// <summary>Commands created and not yet disposed.</summary>
    public int InFlight { get { lock (_sync) return _inFlight; } }

    /// <summary>True once <see cref="Shutdown"/> has started; new commands are refused from then on.</summary>
    public bool IsShuttingDown { get { lock (_sync) return _closing; } }

    public override SqliteCommand CreateCommand()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing && !_inClose, this);
            var command = new GuardedCommand(this) { Connection = this, CommandTimeout = DefaultTimeout, Transaction = Transaction };
            _inFlight++;
            return command;
        }
    }

    /// <summary>Stops new commands, waits up to <paramref name="wait"/> for the ones in flight, then closes.
    /// Returns false when a command was still running: the connection then closes when that command is disposed.</summary>
    public bool Shutdown(TimeSpan wait)
    {
        lock (_sync)
        {
            if (_closing) return _closed;
            _closing = true;
            var clock = Stopwatch.StartNew();
            while (_inFlight > 0)
            {
                var left = wait - clock.Elapsed;
                if (left <= TimeSpan.Zero || !Monitor.Wait(_sync, left) && _inFlight > 0) return false;
            }
            CloseLocked();
            return true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Shutdown(TimeSpan.FromSeconds(10));
        else base.Dispose(false);
    }

    private void CloseLocked()
    {
        if (_closed) return;
        _closed = true;
        _inClose = true;
        try { base.Dispose(true); }
        finally { _inClose = false; }
    }

    private void Released(GuardedCommand command, bool disposing)
    {
        lock (_sync)
        {
            command.DisposeCore(disposing);
            _inFlight--;
            if (_closing && _inFlight == 0)
            {
                CloseLocked();
                Monitor.PulseAll(_sync);
            }
        }
    }

    private sealed class GuardedCommand(GuardedSqliteConnection owner) : SqliteCommand
    {
        private int _released;

        internal void DisposeCore(bool disposing) => base.Dispose(disposing);

        // #161: run under the connection's write lock. The transaction is bound here, under the lock, not at
        // CreateCommand: a command created while another thread's BEGIN was open would otherwise carry that
        // transaction (or, created before it, fail with "requires the command to have a transaction").
        // Close() rolls back with its own command while the internal lock is held: that one skips the write lock.
        public override int ExecuteNonQuery()
        {
            if (owner._inClose) return base.ExecuteNonQuery();
            using (owner.EnterWrite()) { Bind(); return base.ExecuteNonQuery(); }
        }

        public override object? ExecuteScalar()
        {
            if (owner._inClose) return base.ExecuteScalar();
            using (owner.EnterWrite()) { Bind(); return base.ExecuteScalar(); }
        }

        // ExecuteReader() and ExecuteDbDataReader end up here. Rows of a SELECT are stepped after the lock is released;
        // reads cannot be rolled back, and every write statement here is executed by this first call.
        public override SqliteDataReader ExecuteReader(CommandBehavior behavior)
        {
            if (owner._inClose) return base.ExecuteReader(behavior);
            using (owner.EnterWrite()) { Bind(); return base.ExecuteReader(behavior); }
        }

        private void Bind() => Transaction = owner.Transaction;

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _released, 1) == 1) { base.Dispose(disposing); return; }
            owner.Released(this, disposing);
        }
    }
}
