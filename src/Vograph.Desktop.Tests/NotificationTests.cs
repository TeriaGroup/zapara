using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class NotificationTests
{
    [Theory]
    [InlineData("20:00", true)]
    [InlineData("7:30", true)]
    [InlineData("24:00", false)]
    [InlineData("20:60", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Time_Validation(string? value, bool ok) => Assert.Equal(ok, NotificationScheduler.IsValidTime(value));

    [Fact]
    public void Evening_Time_Means_Tomorrow_Morning_Means_Today()
    {
        var now = new DateTime(2026, 9, 6, 20, 0, 0);
        Assert.Equal(new DateTime(2026, 9, 7), NotificationScheduler.TargetDate(now, "20:00", "07:30"));
        Assert.Equal(new DateTime(2026, 9, 6), NotificationScheduler.TargetDate(new DateTime(2026, 9, 6, 7, 30, 0), "20:00", "07:30"));
    }

    [Fact]
    public async Task Tick_Fires_Once_Per_Minute_Only_At_Configured_Times_And_Respects_The_Switch()
    {
        using var db = TestDb.Create();
        var s = db.Services.Db.GetSettings();
        s.NotifyTime1 = "20:00";
        s.NotifyTime2 = "07:30";
        db.Services.Db.SaveSettings(s);
        var scheduler = new NotificationScheduler(db.Services);

        Assert.Null(await scheduler.TickAsync(new DateTime(2026, 9, 6, 19, 59, 0)));
        var text = await scheduler.TickAsync(new DateTime(2026, 9, 6, 20, 0, 10)); // Sunday evening → Monday (odd): Матан, ОСН РОС ГОС
        Assert.NotNull(text);
        Assert.Contains("Матан", text);
        Assert.Contains("ОСН РОС ГОС", text);
        Assert.Contains("[ДЗ!]", text); // the fixture homework is due Monday — burning for the injected clock
        Assert.Null(await scheduler.TickAsync(new DateTime(2026, 9, 6, 20, 0, 40))); // same minute: no repeat
        Assert.Single(db.Services.Toasts.Items, t => t.Text == text);

        db.Services.Prefs.NotificationsEnabled = false;
        Assert.Null(await scheduler.TickAsync(new DateTime(2026, 9, 7, 7, 30, 0)));
        db.Services.Prefs.NotificationsEnabled = true;
        var morning = await scheduler.TickAsync(new DateTime(2026, 9, 7, 7, 30, 0));
        Assert.NotNull(morning);
        Assert.Contains("Матан", morning); // Monday itself

        var test = await scheduler.ShowTestAsync(new DateTime(2026, 9, 6, 12, 0, 0)); // always "tomorrow"
        Assert.NotNull(test);
        Assert.Contains("Матан", test);
        Assert.Equal(1, db.Services.CoreGate.CurrentCount);
    }

    /// <summary>T10 #11: two ticks inside the same minute (a slow build) fire once — the minute is claimed before the await.</summary>
    [Fact]
    public async Task Overlapping_Ticks_Fire_Once()
    {
        using var db = TestDb.Create();
        var s = db.Services.Db.GetSettings();
        s.NotifyTime1 = "20:00";
        db.Services.Db.SaveSettings(s);
        var scheduler = new NotificationScheduler(db.Services);
        var now = new DateTime(2026, 9, 6, 20, 0, 5);

        var first = scheduler.TickAsync(now);
        var second = scheduler.TickAsync(now.AddSeconds(20));

        Assert.NotNull(await first);
        Assert.Null(await second);
        Assert.Single(db.Services.Toasts.Items);
    }

    /// <summary>The «[ДЗ!]» marker follows the injected clock, not the status Core persisted with the real one.</summary>
    [Fact]
    public async Task Burning_Marker_Is_Decided_By_The_Injected_Clock()
    {
        using var db = TestDb.Create();
        var scheduler = new NotificationScheduler(db.Services);

        var far = await scheduler.ShowTestAsync(new DateTime(2026, 8, 30, 12, 0, 0)); // tomorrow = 31.08, homework due 07.09
        Assert.NotNull(far);
        Assert.Contains("Матан", far);
        Assert.DoesNotContain("[ДЗ!]", far);
    }

    /// <summary>
    /// The timer posts every tick to the UI thread (Start → Dispatcher.UIThread.Post), and Avalonia raises
    /// desktop.Exit on that same thread, where AppServices.Dispose blocks inside CoreGate.Wait(2 s). A gate
    /// awaited from the UI thread hands its release to a dispatcher continuation the blocked thread can never
    /// pump — the shape ViewModelBase.GatedAsync exists to avoid — so an Exit landing inside a tick stalled
    /// shutdown for the full two seconds and closed the database anyway. The gate is held on purpose here so the
    /// tick is guaranteed to park on it rather than race through: once the holder lets go, the work and its
    /// release must both happen off the UI thread, and Dispose must be able to take the gate straight after.
    /// </summary>
    [AvaloniaFact]
    public async Task Dispose_During_A_Tick_Does_Not_Wait_For_The_Blocked_Dispatcher()
    {
        using var db = TestDb.Create();
        var services = db.Services;
        var s = services.Db.GetSettings();
        s.NotifyTime1 = "20:00";
        services.Db.SaveSettings(s);
        var scheduler = new NotificationScheduler(services);

        var held = new ManualResetEventSlim();
        var holder = Task.Run(async () =>
        {
            await services.CoreGate.WaitAsync();
            held.Set();
            await Task.Delay(250);
            services.CoreGate.Release();
        });
        held.Wait(TestContext.Current.CancellationToken);

        Assert.True(Dispatcher.UIThread.CheckAccess()); // the thread the timer posts to and the one Exit runs on
        var tick = scheduler.TickAsync(new DateTime(2026, 9, 6, 20, 0, 5)); // parks on the gate, held by the pool

        var sw = System.Diagnostics.Stopwatch.StartNew();
        services.Dispose(); // blocks this thread: from here on nothing queued on the dispatcher can run
        sw.Stop();

        // AppLog writes its first line lazily, so a shutdown that had nothing to complain about leaves no file.
        var log = File.Exists(services.Log.CurrentFile) ? File.ReadAllText(services.Log.CurrentFile) : "";
        Assert.DoesNotContain("closing the database anyway", log);
        Assert.InRange(sw.ElapsedMilliseconds, 100, 1800); // the 2 s timeout is the stall this pins
        await holder;
        // The tick may also be abandoned rather than finished: SemaphoreSlim.Dispose leaves a pending WaitAsync
        // parked for good, which is precisely the shutdown race TickAsync's ObjectDisposedException branch is
        // written for. Either way it must never fault — a timer callback that threw would take the process down.
        await Task.WhenAny(tick, Task.Delay(1000, TestContext.Current.CancellationToken));
        Assert.False(tick.IsFaulted, tick.Exception?.ToString());
    }

    [Fact]
    public async Task Tick_Never_Throws_When_Core_Is_Gone()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        var services = AppServices.Create(dir);
        var scheduler = new NotificationScheduler(services);
        services.Dispose();
        Assert.Null(await scheduler.TickAsync(new DateTime(2026, 9, 6, 20, 0, 0)));
        try { Directory.Delete(dir, recursive: true); } catch (IOException ex) { Console.Error.WriteLine($"temp dir left behind ({dir}): {ex.Message}"); }
    }
}
