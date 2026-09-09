using System.Globalization;
using System.Text;
using Avalonia.Threading;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Domain;

namespace Vograph.Desktop.Services;

/// <summary>
/// The two daily notification times (evening → tomorrow, morning → today) become in-app toasts. Ticks every
/// 30 s on a background timer, reads Core under the gate, posts the toast to the UI thread. Nothing here throws:
/// a timer callback that throws would take the process down.
/// </summary>
public sealed class NotificationScheduler : IDisposable
{
    private readonly AppServices _app;
    private readonly Func<DateTime> _clock;
    private System.Threading.Timer? _timer;
    private string? _lastFired;
    private string? _inFlight;

    public NotificationScheduler(AppServices app, Func<DateTime>? clock = null)
    {
        _app = app;
        _clock = clock ?? (() => DateTime.Now);
    }

    public static bool IsValidTime(string? value) =>
        !string.IsNullOrWhiteSpace(value) && TimeSpan.TryParseExact(value.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var t) && t < TimeSpan.FromDays(1);

    /// <summary>Time 1 (evening) announces tomorrow, time 2 (morning) announces today — WPF's LogAndShow rule.</summary>
    public static DateTime TargetDate(DateTime now, string? time1, string? time2)
    {
        var hm = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        return hm == Normalize(time1) ? now.Date.AddDays(1) : now.Date;
    }

    private static string? Normalize(string? t) =>
        TimeSpan.TryParseExact(t?.Trim() ?? "", new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var ts) ? ts.ToString(@"hh\:mm") : null;

    public void Start()
    {
        if (!_app.Work.IsAccepting) return;
        using (ExecutionContext.SuppressFlow())
            _timer ??= new System.Threading.Timer(_ => PostTick(a => Dispatcher.UIThread.Post(a)), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
    }

    public bool IsRunning => _timer is not null;
    internal void PostTick(Action<Action> post) => _app.Work.Post(post, async () => await TickAsync(_clock()), ex => _app.Log.Error("notification callback", ex));

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Shows the notification when <paramref name="now"/> matches a configured time (once per minute). Returns the text shown, else null.</summary>
    public async Task<string?> TickAsync(DateTime now)
    {
        using var operation = _app.Work.Enter();
        if (!operation.IsCurrent) return null;
        if (!_app.Prefs.NotificationsEnabled) return null;
        var key = now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        if (key == _lastFired || key == _inFlight) return null; // claimed before the await: a slow build cannot fire twice
        _inFlight = key;
        try
        {
            var text = await GatedAsync(() =>
            {
                var s = _app.Db.GetSettings();
                var hm = now.ToString("HH:mm", CultureInfo.InvariantCulture);
                if (Normalize(s.NotifyTime1) != hm && Normalize(s.NotifyTime2) != hm) return null;
                return BuildText(s, TargetDate(now, s.NotifyTime1, s.NotifyTime2), now);
            });
            if (text is null || !operation.IsCurrent) return null;
            _lastFired = key;
            Show(text);
            return text;
        }
        catch (ObjectDisposedException ex)
        {
            _app.Log.Warn($"notification: {ex.Message}"); // shutdown raced the timer
            return null;
        }
        catch (Exception ex)
        {
            _app.Log.Error("notification", ex);
            return null;
        }
        finally
        {
            _inFlight = null;
        }
    }

    /// <summary>«Тест уведомления»: tomorrow's text right now.</summary>
    public async Task<string?> ShowTestAsync(DateTime now)
    {
        using var operation = _app.Work.Enter();
        if (!operation.IsCurrent) return null;
        try
        {
            var text = await GatedAsync(() => BuildText(_app.Db.GetSettings(), now.Date.AddDays(1), now));
            if (text is not null && operation.IsCurrent) Show(text);
            return text;
        }
        catch (Exception ex)
        {
            _app.Log.Error("notification test", ex);
            _app.Toasts.Error($"{_app.Loc.T("errorTitle")}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Core's NotificationService.BuildNotificationText layout, rebuilt here because its «[ДЗ!]» marker reads the
    /// status Core persisted with the real clock. Everything else — day name, parity, numbering, display names —
    /// is Core's; only the marker is recomputed against <paramref name="now"/>. Runs inside the gate.
    /// </summary>
    private string BuildText(Settings settings, DateTime date, DateTime now)
    {
        if (string.IsNullOrEmpty(settings.MyGroupId)) return _app.I18n.T("noLessons");
        var isOdd = ParityCodes.IsOdd(date, settings);
        var header = $"{_app.I18n.FormatDay(date)}, {_app.I18n.FormatParity(isOdd)}: ";

        var lessons = _app.Schedule.GetSchedule(date, settings.MyGroupId);
        if (lessons.Count == 0) return header + _app.I18n.T("notifNoLessons");

        var dayOfWeek = (int)date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek;
        var homework = _app.Homework.GetAll();
        var sb = new StringBuilder(header);
        var n = 1;
        foreach (var l in lessons.OrderBy(x => x.TimeStart))
        {
            var display = _app.Overrides.GetDisplayName(l.SubjectRaw, dayOfWeek);
            var mark = IsBurning(homework, settings, l.SubjectRaw, now) ? $" {_app.I18n.T("notifBurning")}" : "";
            sb.Append($"{n++}. {display} {l.ClassroomRaw}{mark}; ");
        }
        return sb.ToString().TrimEnd(' ', ';');
    }

    /// <summary>Open homework of that subject that is due today or tomorrow — measured from the injected clock.</summary>
    private bool IsBurning(IReadOnlyList<Homework> homework, Settings settings, string subjectRaw, DateTime now)
    {
        var normalized = ParityService.NormalizeSubject(subjectRaw);
        foreach (var hw in homework)
        {
            if (hw.SubjectRawNormalized != normalized || hw.Status == "done" || hw.DueDateComputed is not { } due) continue;
            var lessonsBefore = HomeworkLabels.LessonsUntil(_app.Db, settings, hw.SubjectRawNormalized, now.Date, due);
            if (HomeworkStatus.Compute(hw, now.Date, lessonsBefore) is "burning" or "burning_urgent") return true;
        }
        return false;
    }

    private void Show(string text)
    {
        _app.Toasts.Show(text, ToastKind.Info, 15000);
        _app.Log.Info("notification shown");
    }

    /// <summary>
    /// ViewModelBase.GatedAsync's shape, for the same reason it has it. Both entry points are reached from the UI
    /// thread — the timer posts every tick to the dispatcher, «Тест уведомления» is a button — and Avalonia raises
    /// desktop.Exit on that thread, where AppServices.Dispose blocks inside CoreGate.Wait(2 s). Awaiting the gate
    /// from the UI thread captures the dispatcher's SynchronizationContext, so the continuation that releases it
    /// is posted back to a thread that is no longer pumping: Dispose could only ever time out, stall shutdown for
    /// two seconds and close the database anyway. Taking the gate, running the work and releasing it all inside
    /// one Task.Run puts the release on the pool, where a blocked UI thread cannot hold it up.
    /// </summary>
    private async Task<T?> GatedAsync<T>(Func<T?> work) where T : class
    {
        using var operation = _app.Work.Enter();
        if (!operation.IsCurrent) return null;
        try { return await Task.Run(async () =>
        {
            await _app.CoreGate.WaitAsync(operation.Token).ConfigureAwait(false); // acquired on the pool
            try { operation.ThrowIfStale(); return work(); }
            finally { _app.CoreGate.Release(); } // released on the pool — a UI-thread Dispose() can proceed
        }); }
        catch (OperationCanceledException) when (!operation.IsCurrent) { return null; }
    }

    public void Dispose() => Stop();
}
