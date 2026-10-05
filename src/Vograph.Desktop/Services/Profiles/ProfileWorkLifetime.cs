namespace Vograph.Desktop.Services.Profiles;

/// <summary>Admission is linearized with invalidation. A lease owns the OUTER operation, not just its SQL.</summary>
public sealed class ProfileWorkLifetime
{
    private readonly object gate = new();
    private readonly AsyncLocal<Work?> ambient = new();
    private CancellationTokenSource cancellation = new();
    private readonly List<CancellationTokenSource> retired = [];
    private Task cancelled = Task.CompletedTask;
    private TaskCompletionSource idle = Completed();
    private long generation;
    private int outstanding;
    private bool accepting = true;
    private bool closed;
    public bool IsAccepting { get { lock (gate) return accepting; } }
    public int Outstanding { get { lock (gate) return outstanding; } }
    public bool CanPublish { get { lock (gate) return accepting && (ambient.Value is not { } work || Current(work)); } }

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    public Work Enter()
    {
        var work = Reserve();
        work.Previous = ambient.Value;
        ambient.Value = work;
        return work;
    }

    private Work Reserve()
    {
        lock (gate)
        {
            var admitted = accepting && (ambient.Value is not { } parent || Current(parent));
            if (admitted && outstanding++ == 0) idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new Work(this, generation, closed ? new CancellationToken(true) : cancellation.Token, admitted);
        }
    }

    private bool Current(Work work) => work.Admitted && !work.Released && accepting && work.Generation == generation;
    public void Suspend()
    {
        CancellationTokenSource old;
        lock (gate)
        {
            if (!accepting) return;
            accepting = false;
            generation++;
            old = cancellation;
            retired.Add(old);
            // CancelAsync never runs user cancellation callbacks under the admission lock.
            cancelled = Task.WhenAll(cancelled, old.CancelAsync());
        }
    }

    public void Resume()
    {
        lock (gate)
        {
            if (accepting || closed) return;
            cancellation = new();
            generation++;
            accepting = true;
        }
    }

    public Task WhenIdleAsync(CancellationToken ct = default)
    {
        lock (gate) return Task.WhenAll(idle.Task, cancelled).WaitAsync(ct);
    }

    internal void Retire()
    {
        lock (gate)
        {
            if (accepting || outstanding != 0 || !cancelled.IsCompleted)
                throw new InvalidOperationException("Профиль ещё выполняет работы.");
            closed = true;
            foreach (var source in retired.Distinct()) source.Dispose();
            retired.Clear();
        }
    }

    /// <summary>Registers before posting. Cancellation cannot release a callback still owned by a dispatcher.</summary>
    public void Post(Action<Action> post, Func<Task> action, Action<Exception> report)
    {
        var work = Reserve();
        if (!work.IsCurrent) { work.Dispose(); return; }
        try { post(async () => await InvokeAsync()); }
        catch { work.Dispose(); throw; }
        async Task InvokeAsync()
        {
            work.Previous = ambient.Value;
            ambient.Value = work;
            using (work)
            {
                try { if (work.IsCurrent) await action(); }
                catch (OperationCanceledException) when (!work.IsCurrent) { }
                catch (Exception ex) { report(ex); }
            }
        }
    }

    public sealed class Work : IDisposable
    {
        private readonly ProfileWorkLifetime owner;
        internal readonly long Generation;
        internal readonly bool Admitted;
        internal bool Released;
        internal Work? Previous;
        internal Work(ProfileWorkLifetime owner, long generation, CancellationToken token, bool admitted)
        { this.owner = owner; Generation = generation; Token = token; Admitted = admitted; }
        public CancellationToken Token { get; }
        public bool IsCurrent { get { lock (owner.gate) return owner.Current(this); } }
        public void ThrowIfStale() { if (!IsCurrent) throw new OperationCanceledException(Token); }
        public void Dispose()
        {
            if (ReferenceEquals(owner.ambient.Value, this)) owner.ambient.Value = Previous;
            lock (owner.gate)
            {
                if (Released) return;
                Released = true;
                if (Admitted && --owner.outstanding == 0) owner.idle.TrySetResult();
            }
        }
    }
}
