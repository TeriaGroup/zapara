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
    private bool _inClose; // Close() rolls an open transaction back with its own ROLLBACK command

    public GuardedSqliteConnection(string connectionString) : base(connectionString) { }

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

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _released, 1) == 1) { base.Dispose(disposing); return; }
            owner.Released(this, disposing);
        }
    }
}
