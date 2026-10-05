using Vograph.Desktop.Services;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class NotificationPrivacyTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Private_toast_payload_never_persists_in_shared_logs_across_profiles(bool manual)
    {
        var harness = new ProfileHarness();
        var root = harness.Directory.Root;
        try
        {
            var coordinator = harness.Coordinator;
            Assert.True((await coordinator.LoginAsync("Test.User", Password, TestContext.Current.CancellationToken)).Committed);
            var a = coordinator.Current.Services;
            var canaryA = "private-override-A-" + Guid.NewGuid().ToString("N");
            var textA = await ShowAsync(a, canaryA, manual);
            var logsA = ReadLogs(root);

            harness.Handler.Send = (_, _) => Task.FromResult(Json(Session(2, Guid.NewGuid(), Guid.NewGuid())));
            Assert.True((await coordinator.LoginAsync("Test.User", Password, TestContext.Current.CancellationToken)).Committed);
            var b = coordinator.Current.Services;
            Assert.Same(a.Log, b.Log);
            Assert.True(a.IsClosed);
            Assert.Empty(b.Db.GetOverrides());
            var logsAfterSwitch = ReadLogs(root);
            var canaryB = "private-override-B-" + Guid.NewGuid().ToString("N");
            var textB = await ShowAsync(b, canaryB, manual);
            Assert.DoesNotContain(canaryA, textB);
            var logsB = ReadLogs(root);

            Assert.True((await coordinator.LogoutAsync(TestContext.Current.CancellationToken)).Committed);
            var guest = coordinator.Current.Services;
            Assert.True(guest.Profile.IsGuest);
            Assert.True(b.IsClosed);
            Assert.Same(a.Log, guest.Log);
            Assert.Empty(guest.Db.GetOverrides());
            Assert.Empty(guest.Toasts.Items);
            var logsGuest = ReadLogs(root);

            foreach (var logs in new[] { logsA, logsAfterSwitch, logsB, logsGuest })
            {
                Assert.DoesNotContain(canaryA, logs);
                Assert.DoesNotContain(canaryB, logs);
                Assert.DoesNotContain(textA, logs);
                Assert.DoesNotContain(textB, logs);
                Assert.DoesNotContain("[ДЗ!]", logs);
            }
        }
        finally
        {
            await harness.DisposeAsync();
            Assert.False(Directory.Exists(root));
            output.WriteLine($"Cleanup verified: {root}; owned SQLite/log root removed; memory vault disposed.");
        }
    }

    private static async Task<string> ShowAsync(AppServices app, string canary, bool manual)
    {
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-timetable.xml"));
        await app.Parser.RefreshAsync(xmlOverride: xml);
        var settings = app.Db.GetSettings();
        settings.MyGroupId = TestDb.MyGroupId;
        settings.NotifyTime1 = "20:00";
        app.Db.SaveSettings(settings);
        app.Prefs.NotificationsEnabled = true;
        app.Overrides.AddOrUpdate(TestDb.MathSubject, "global", canary, "synthetic private note");
        app.Homework.AddHomework(TestDb.MathSubject, "synthetic private homework", 1,
            createdAt: new DateTime(2026, 9, 5, 12, 0, 0));
        using var scheduler = new NotificationScheduler(app);
        var now = new DateTime(2026, 9, 6, 20, 0, 0);
        var text = manual ? await scheduler.ShowTestAsync(now) : await scheduler.TickAsync(now);
        Assert.NotNull(text);
        Assert.Contains(canary, text);
        Assert.Contains("[ДЗ!]", text);
        Assert.Equal(text, Assert.Single(app.Toasts.Items).Text);
        if (!manual) Assert.Null(await scheduler.TickAsync(now.AddSeconds(20)));
        Assert.Single(app.Toasts.Items);
        return text;
    }

    private static string ReadLogs(string root) => string.Join(Environment.NewLine,
        Directory.EnumerateFiles(root, "*.log", SearchOption.AllDirectories).Select(File.ReadAllText));
}
