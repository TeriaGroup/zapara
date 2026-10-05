namespace Zapara.Server.Accounts.ExternalProviders;

internal sealed class ExternalProviderTestsClock : TimeProvider
{
    private TimeSpan elapsed;
    private readonly List<ManualTimer> timers = [];
    internal int LiveTimers => timers.Count(t => !t.Disposed);
    internal void Advance(TimeSpan amount)
    {
        elapsed += amount;
        foreach (var timer in timers.ToArray()) timer.Fire(elapsed);
    }
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        timers.Add(timer);
        return timer;
    }
    private sealed class ManualTimer(ExternalProviderTestsClock clock, TimerCallback callback, object? state) : ITimer
    {
        internal bool Disposed { get; private set; }
        private TimeSpan due;
        private TimeSpan period;
        internal void Fire(TimeSpan now)
        {
            if (Disposed || now < due) return;
            due = period == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : now + period;
            callback(state);
        }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (Disposed) return false;
            due = dueTime == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : clock.elapsed + dueTime;
            this.period = period;
            return true;
        }
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
