namespace Vograph.Desktop.Services.Profiles;

/// <summary>Unlike Progress&lt;T&gt;, owns queued UI callbacks before dispatch and rejects the originating generation.</summary>
public sealed class ProfileProgress<T>(ProfileWorkLifetime lifetime, ProfileWorkLifetime.Work operation,
    Action<T> publish, Action<Exception> reportError) : IProgress<T>
{
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private readonly object gate = new();
    private long sequence, delivered;
    public void Report(T value)
    {
        if (!operation.IsCurrent) return;
        var stamp = Interlocked.Increment(ref sequence);
        lifetime.Post(Post, () =>
        {
            // This callback has its own registered lease. The producing operation may already have returned.
            lock (gate)
                if (stamp > delivered) { delivered = stamp; publish(value); }
            return Task.CompletedTask;
        }, reportError);
    }
    private void Post(Action action)
    {
        if (context is not null) context.Post(_ => action(), null);
        else ThreadPool.QueueUserWorkItem(_ => action());
    }
}
