using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vograph.Desktop.Services;

public enum ToastKind { Info, Ok, Warn, Bad }

public sealed partial class ToastItem : ObservableObject
{
    public ToastItem(string text, ToastKind kind, TimeSpan duration)
    {
        Text = text;
        Kind = kind;
        Duration = duration;
    }

    public string Text { get; }
    public ToastKind Kind { get; }
    public TimeSpan Duration { get; }

    /// <summary>Hovering a toast keeps it on screen.</summary>
    [ObservableProperty] private bool _isPaused;

    public bool IsOk => Kind == ToastKind.Ok;
    public bool IsWarn => Kind == ToastKind.Warn;
    public bool IsBad => Kind == ToastKind.Bad;
}

/// <summary>Bottom-right transient messages. Newest first, at most three, auto-hide after Duration.</summary>
public sealed class ToastService : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<ToastItem, PendingToast> _pending = new();
    private readonly Func<TimeSpan, Action, IDisposable> _schedule;
    private readonly Func<bool> _canPublish;
    private bool _disposed;

    public ToastService(Func<TimeSpan, Action, IDisposable>? schedule = null, Func<bool>? canPublish = null)
    {
        _schedule = schedule ?? DefaultSchedule;
        _canPublish = canPublish ?? (() => true);
    }

    /// <summary>
    /// Plain timer: arming must not touch the Avalonia dispatcher (unit tests run without a platform);
    /// the callback is marshalled to the UI thread only when it fires.
    /// </summary>
    private IDisposable DefaultSchedule(TimeSpan delay, Action action)
    {
        System.Threading.Timer? timer = null;
        timer = new System.Threading.Timer(_ =>
        {
            // Dispose and dispatch share a gate: an already-running timer cannot post
            // into a dispatcher after its owning service has finished closing.
            lock (_gate)
            {
                timer?.Dispose();
                if (_disposed) return;
                Dispatcher.UIThread.Post(action);
            }
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try
        {
            timer.Change(delay, Timeout.InfiniteTimeSpan);
            return timer;
        }
        catch
        {
            timer.Dispose();
            throw;
        }
    }

    public ObservableCollection<ToastItem> Items { get; } = new();

    public void Show(string text, ToastKind kind = ToastKind.Info, int ms = 4000)
    {
        lock (_gate)
        {
            if (_disposed || !_canPublish()) return;
            var item = new ToastItem(text, kind, TimeSpan.FromMilliseconds(ms));
            Items.Insert(0, item);
            while (Items.Count > 3) Dismiss(Items[^1]);
            Arm(item);
        }
    }

    public void Info(string text) => Show(text, ToastKind.Info);
    public void Ok(string text) => Show(text, ToastKind.Ok);
    public void Warn(string text) => Show(text, ToastKind.Warn, 6000);
    public void Error(string text) => Show(text, ToastKind.Bad, 8000);

    public void Dismiss(ToastItem item)
    {
        lock (_gate)
        {
            CancelPending(item);
            Items.Remove(item);
        }
    }

    private void Arm(ToastItem item)
    {
        if (_disposed || !Items.Contains(item)) return;
        var pending = new PendingToast();
        _pending.Add(item, pending);
        try
        {
            pending.Attach(_schedule(item.Duration, () =>
            {
                lock (_gate)
                {
                    if (_disposed || !_pending.TryGetValue(item, out var current) || current != pending) return;
                    CancelPending(item);
                    if (_disposed || !Items.Contains(item)) return;
                    if (item.IsPaused) { Arm(item); return; }
                    Items.Remove(item);
                }
            }));
        }
        catch
        {
            CancelPending(item);
            Items.Remove(item);
            throw;
        }
    }

    private void CancelPending(ToastItem item)
    {
        if (_pending.Remove(item, out var pending)) pending.Cancel();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            var pending = _pending.Values.ToArray();
            _pending.Clear();
            foreach (var registration in pending) registration.Cancel();
            // Retirement can run on a worker after the view is detached. Close resources
            // without touching its bound collection or posting work during UI teardown.
        }
    }

    private sealed class PendingToast
    {
        private IDisposable? _handle;
        private bool _cancelled;

        public void Attach(IDisposable handle)
        {
            // Cancellation may happen reentrantly before Schedule returns its handle.
            if (_cancelled) handle.Dispose();
            else _handle = handle;
        }

        public void Cancel()
        {
            if (_cancelled) return;
            _cancelled = true;
            var handle = _handle;
            _handle = null;
            handle?.Dispose();
        }
    }
}
