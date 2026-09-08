using Npgsql;

namespace Zapara.Server.Timetable;

public sealed class RefreshLease : IAsyncDisposable
{
    private readonly SnapshotStore owner;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;
    private bool terminal;
    internal NpgsqlConnection Connection { get; }
    public Guid AttemptId { get; }

    internal RefreshLease(SnapshotStore owner, NpgsqlConnection connection, Guid attemptId)
        => (this.owner, Connection, AttemptId) = (owner, connection, attemptId);

    internal async Task EnterAsync(SnapshotStore store, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        if (!ReferenceEquals(owner, store) || disposed || terminal)
        {
            gate.Release();
            throw new InvalidOperationException("Недействительная аренда обновления.");
        }
    }

    internal void Exit() => gate.Release();
    internal void Complete() => terminal = true;

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync(CancellationToken.None);
        try
        {
            if (disposed) return;
            disposed = true;
            // Nonpooled session closure releases all session locks, including after transport failure.
            await Connection.DisposeAsync();
        }
        catch (NpgsqlException) { throw new StoreException(FailureCode.DbUnavailable, AttemptId); }
        finally { gate.Release(); }
    }
}
