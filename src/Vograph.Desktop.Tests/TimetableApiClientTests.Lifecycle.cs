using Vograph.Core.Services;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public partial class TimetableApiClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_disable_cancels_catalog_and_reenable_requires_fresh_refresh(bool reenableBeforeRelease)
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        var catalogs = 0;
        var groups = 0;
        using var handler = new TransportHandler(async (request, ct) =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/groups"))
            {
                Interlocked.Increment(ref groups);
                return Json(Schedule(pin: NewPin));
            }
            if (Interlocked.Increment(ref catalogs) == 1)
            {
                using var registration = ct.Register(() => cancelled = true);
                arrived.SetResult();
                await release.Task; // Deliberately late handler: cancellation must remain latched.
                return Json(Catalog());
            }
            return Json(Catalog(NewPin));
        });
        using var http = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "vograph-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var app = AppServices.Create(dir, () => false, "https://example.invalid/", uri => new(http, uri));
            var settings = app.Db.GetSettings(); settings.MyGroupId = "a"; app.Db.SaveSettings(settings);
            var before = TimetableApiCacheTests.Dump(app.Db);
            var pending = app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken);
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            app.AllowNetwork = false;
            var observedOnDisable = cancelled;
            if (reenableBeforeRelease) app.AllowNetwork = true;
            release.SetResult();
            var changed = await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(observedOnDisable, "Disabling network must cancel the in-flight catalog request immediately.");
            Assert.False(changed);
            Assert.Equal(0, groups);
            Assert.Equal(1, catalogs);
            Assert.Equal(before, TimetableApiCacheTests.Dump(app.Db));
            app.AllowNetwork = true;
            Assert.True(await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
            Assert.Equal(2, catalogs);
            Assert.Equal(1, groups);
            Assert.Equal(Guid.Parse(NewPin), new TimetableApiCache(app.Db).Read("a")!.Meta!.SnapshotId);
        }
        finally { release.TrySetResult(); CleanupApiDirectory(dir); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_pending_start_stops_without_late_navigation_or_timer(bool dispose)
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new TransportHandler(async (_, _) =>
        {
            arrived.TrySetResult();
            await release.Task;
            return Json(Catalog());
        });
        using var http = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "vograph-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var app = AppServices.Create(dir, () => false, "https://example.invalid/", uri => new(http, uri));
            // Retained catalog makes a late continuation reach navigation/timer, not only the error branch.
            app.Db.UpsertGroup(new Vograph.Core.Models.Group { Id = "a", Name = "Test" });
            var settings = app.Db.GetSettings();
            settings.AutoUpdate = false;
            settings.LastAutoCheckAt = DateTime.UtcNow.ToString("O");
            app.Db.SaveSettings(settings);
            var shell = new ShellViewModel(app);
            try
            {
                var current = shell.Current;
                var pending = shell.StartAsync();
                await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                shell.Stop();
                if (dispose) app.Dispose();
                release.SetResult();
                await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Same(current, shell.Current);
                Assert.False(shell.IsAutoCheckRunning);
                await shell.StartAsync();
                Assert.Same(current, shell.Current);
                Assert.False(shell.IsAutoCheckRunning);
                Assert.False(File.Exists(app.Log.CurrentFile), "Stopped startup must not log disposed database/gate access.");
            }
            finally { shell.Stop(); }
        }
        finally { release.TrySetResult(); CleanupApiDirectory(dir); }
    }
}
