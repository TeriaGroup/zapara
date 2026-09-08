namespace Vograph.Desktop.Services.Profiles;

/// <summary>Proof that admission is closed, outer operations drained, and the graph's CoreGate is held.</summary>
public sealed class ExclusiveProfileLease : IDisposable
{
    internal AppServices Owner { get; }
    internal bool Released { get; private set; }
    private ExclusiveProfileLease(AppServices owner) => Owner = owner;
    public static async Task<ExclusiveProfileLease> AcquireAsync(AppServices app, CancellationToken ct)
    {
        if (app.Work.IsAccepting) throw new InvalidOperationException("Сначала остановите приём работ.");
        await app.Work.WhenIdleAsync(ct).ConfigureAwait(false);
        await app.CoreGate.WaitAsync(ct).ConfigureAwait(false);
        if (app.Work.IsAccepting || app.Work.Outstanding != 0)
        {
            app.CoreGate.Release();
            throw new InvalidOperationException("Профиль снова принимает работы.");
        }
        return new(app);
    }
    public void Dispose()
    {
        if (Released) return;
        Released = true;
        Owner.CoreGate.Release();
        if (Owner.IsClosed) Owner.CoreGate.Dispose();
    }
}
