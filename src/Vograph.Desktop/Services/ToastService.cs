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
public sealed class ToastService
{
    private readonly Func<TimeSpan, Action, IDisposable> _schedule;

    public ToastService(Func<TimeSpan, Action, IDisposable>? schedule = null) =>
        _schedule = schedule ?? DefaultSchedule;

    /// <summary>
    /// Plain timer: arming must not touch the Avalonia dispatcher (unit tests run without a platform);
    /// the callback is marshalled to the UI thread only when it fires.
    /// </summary>
    private static IDisposable DefaultSchedule(TimeSpan delay, Action action)
    {
        System.Threading.Timer? timer = null;
        timer = new System.Threading.Timer(_ =>
        {
            timer?.Dispose();
            Dispatcher.UIThread.Post(action);
        }, null, delay, Timeout.InfiniteTimeSpan);
        return timer;
    }

    public ObservableCollection<ToastItem> Items { get; } = new();

    public void Show(string text, ToastKind kind = ToastKind.Info, int ms = 4000)
    {
        var item = new ToastItem(text, kind, TimeSpan.FromMilliseconds(ms));
        Items.Insert(0, item);
        while (Items.Count > 3) Items.RemoveAt(Items.Count - 1);
        Arm(item);
    }

    public void Info(string text) => Show(text, ToastKind.Info);
    public void Ok(string text) => Show(text, ToastKind.Ok);
    public void Warn(string text) => Show(text, ToastKind.Warn, 6000);
    public void Error(string text) => Show(text, ToastKind.Bad, 8000);

    public void Dismiss(ToastItem item) => Items.Remove(item);

    private void Arm(ToastItem item)
    {
        // The schedule contract returns a disposable "cancel this pending fire" handle.
        // It must be released once this slot has fired, or a re-arm (paused toast) leaves
        // a stale entry behind in the scheduler.
        IDisposable? handle = null;
        handle = _schedule(item.Duration, () =>
        {
            handle?.Dispose();
            if (!Items.Contains(item)) return;
            if (item.IsPaused) { Arm(item); return; }
            Items.Remove(item);
        });
    }
}
