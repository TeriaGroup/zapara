using Microsoft.Data.Sqlite;
using Vograph.Desktop.Services.Profiles;

namespace Vograph.Desktop.Services;

public sealed partial class AppServices
{
    public Task CloseQuiescedAsync(ExclusiveProfileLease lease)
    {
        if (!ReferenceEquals(lease.Owner, this) || lease.Released || Work.IsAccepting || Work.Outstanding != 0)
            throw new InvalidOperationException("Закрытие требует эксклюзивного владения остановленным профилем.");
        if (_disposed) return Task.CompletedTask;
        _disposed = true;
        PrivateSync?.Dispose();
        Api.Dispose();
        Refresher.Dispose();
        NotificationScheduler.Dispose();
        LanSync.Dispose();
        SqliteConnection.ClearPool(Db.Connection);
        Db.Dispose();
        Work.Retire();
        return Task.CompletedTask;
    }

    internal async Task CloseCandidateAsync()
    {
        Work.Suspend();
        using var lease = await ExclusiveProfileLease.AcquireAsync(this, CancellationToken.None).ConfigureAwait(false);
        await CloseQuiescedAsync(lease).ConfigureAwait(false);
    }
}
