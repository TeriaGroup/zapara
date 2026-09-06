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

    /// <summary>The «[ДЗ!]» marker follows the injected clock, not the status Core persisted with the real one.</summary>
    [Fact]
    public async Task Burning_Marker_Is_Decided_By_The_Injected_Clock()
    {
        using var db = TestDb.Create();
        var scheduler = new NotificationScheduler(db.Services);

        var far = await scheduler.ShowTestAsync(new DateTime(2026, 8, 30, 12, 0, 0)); // tomorrow = 31.08, homework due 07.09
        Assert.NotNull(far);
        Assert.DoesNotContain("[ДЗ!]", far);
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
