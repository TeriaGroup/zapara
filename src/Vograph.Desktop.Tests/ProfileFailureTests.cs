using System.Data;
using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Profiles;
using Vograph.Desktop.ViewModels;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class ProfileFailureTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Noncooperative_outer_callback_times_out_then_remains_stale_after_resume()
    {
        await using var h = new ProfileHarness();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new Probe(h.Guest);
        var operation = probe.AfterAsync(release.Task);
        Assert.Equal(1, h.Guest.Work.Outstanding);
        var result = await h.Coordinator.LoginAsync("Test.User", Password, CT);
        Assert.Equal(ProfileFailure.Busy, result.Snapshot.Failure);
        Assert.False(h.Coordinator.Current.Shell.IsStopped);
        Assert.True(h.Guest.Work.IsAccepting);
        Assert.Null(((AccountMemoryVault)h.Vault).Entry);
        Assert.Equal(ConnectionState.Open, h.Guest.Db.Connection.State);
        release.SetResult();
        Assert.False(await operation);
        Assert.NotEqual("stale", h.Guest.Settings.MyGroupId);
        Assert.False(probe.Published);
        await h.Coordinator.Current.Shell.RefreshGroupCardAsync();
        Assert.True(await probe.AfterAsync(Task.CompletedTask));
    }

    [Fact]
    public async Task Held_CoreGate_timeout_resumes_timers_and_same_shell_is_usable()
    {
        await using var h = new ProfileHarness();
        h.Guest.NotificationScheduler.Start();
        h.Coordinator.Current.Shell.StartAutoCheck();
        await h.Guest.CoreGate.WaitAsync(CT);
        try
        {
            var result = await h.Coordinator.LoginAsync("Test.User", Password, CT);
            Assert.Equal(ProfileFailure.Busy, result.Snapshot.Failure);
            Assert.True(h.Guest.NotificationScheduler.IsRunning);
            Assert.True(h.Coordinator.Current.Shell.IsAutoCheckRunning);
        }
        finally { h.Guest.CoreGate.Release(); }
        Assert.True(await new Probe(h.Guest).AfterAsync(Task.CompletedTask));
        await h.Coordinator.Current.Shell.RefreshGroupCardAsync();
    }

    [Fact]
    public async Task Vault_write_failure_closes_only_prepared_candidate_and_preserves_identity()
    {
        AppServices? candidate = null;
        await using var h = new ProfileHarness(factory: (old, profile) => candidate = old.CreateProfile(profile));
        ((AccountMemoryVault)h.Vault).FailReady = true;
        var result = await h.Coordinator.LoginAsync("Test.User", Password, CT);
        Assert.Equal(ProfileFailure.Credentials, result.Snapshot.Failure);
        Assert.NotNull(candidate);
        Assert.True(candidate.IsClosed);
        Assert.False(h.Guest.IsClosed);
        Assert.True(h.Guest.Work.IsAccepting);
        Assert.Null(((AccountMemoryVault)h.Vault).Entry);
    }

    [Fact]
    public async Task Candidate_constructor_failure_releases_its_SQLite_handle_without_touching_guest()
    {
        using var directory = new ProfileTestDirectory();
        var fail = false;
        using var app = AppServices.Create(directory.Root, () => false, "http://127.0.0.1/",
            _ => fail ? throw new IOException("candidate fixture failure") : new(new HttpClient(), new Uri("http://127.0.0.1/")));
        var descriptor = ProfileDescriptor.Account(directory.Root, new string('A', 64), Guid.NewGuid());
        fail = true;
        await Assert.ThrowsAsync<IOException>(() => Task.Run(() => app.CreateProfile(descriptor), CT));
        using var file = File.Open(descriptor.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(ConnectionState.Open, app.Db.Connection.State);
        Assert.Same(app.Loc, Loc.Current);
    }

    [Fact]
    public async Task Postcommit_publication_failure_is_failclosed_and_recovery_uses_committed_profile()
    {
        var fail = true;
        await using var h = new ProfileHarness(publish: _ => { if (fail) throw new InvalidOperationException("synthetic UI fault"); });
        var result = await h.Coordinator.LoginAsync("Test.User", Password, CT);
        Assert.True(result.Committed);
        Assert.Equal(ProfilePhase.RecoveryRequired, result.Snapshot.Phase);
        Assert.False(h.Guest.Work.IsAccepting);
        Assert.False(h.Coordinator.Current.Services.Work.IsAccepting);
        Assert.Equal(UserId, ((AccountMemoryVault)h.Vault).Entry!.UserId);
        fail = false;
        Assert.Equal(ProfilePhase.Idle, (await h.Coordinator.RecoverAsync(CT)).Phase);
        Assert.True(h.Guest.IsClosed);
        Assert.True(h.Coordinator.Current.Services.Work.IsAccepting);
    }

    [Fact]
    public void Broken_SQLite_candidate_constructor_does_not_leave_an_open_handle()
    {
        using var directory = new ProfileTestDirectory();
        using var app = AppServices.Create(directory.Root, () => false);
        var profile = ProfileDescriptor.Account(directory.Root, new string('A', 64), Guid.NewGuid());
        Directory.CreateDirectory(Path.GetDirectoryName(profile.DatabasePath)!);
        File.WriteAllText(profile.DatabasePath, "not a sqlite database");
        try
        {
            Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => app.CreateProfile(profile));
            using var exclusive = File.Open(profile.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(ConnectionState.Open, app.Db.Connection.State);
        }
        finally
        {
            // RED also cleans the deliberately failed constructor's finalizable SQLite handle.
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private sealed class Probe(AppServices app) : ViewModelBase(app)
    {
        internal bool Published;
        internal Task<bool> SqlAsync(TaskCompletionSource entered, Task wait) => RunAsync(async () =>
        {
            entered.TrySetResult();
            await wait;
            var settings = App.Db.GetSettings();
            settings.MyGroupId = "legitimate-old-SQL";
            App.Db.SaveSettings(settings);
        }, "profile SQL test");
        internal async Task<bool> AfterAsync(Task wait)
        {
            using var operation = App.Work.Enter();
            if (!operation.IsCurrent) return false;
            await wait;
            var wrote = await RunAsync(() => { var s = App.Db.GetSettings(); s.MyGroupId = "stale"; App.Db.SaveSettings(s); }, "profile test");
            if (operation.IsCurrent) Published = true;
            return wrote;
        }
    }

    [Fact]
    public async Task Already_running_SQL_finishes_in_old_DB_on_busy_rollback_but_does_not_publish()
    {
        await using var h = new ProfileHarness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sql = new Probe(h.Guest).SqlAsync(entered, release.Task);
        try
        {
            await entered.Task.WaitAsync(CT);
            Assert.Equal(ProfileFailure.Busy, (await h.Coordinator.LoginAsync("Test.User", Password, CT)).Snapshot.Failure);
            Assert.Null(((AccountMemoryVault)h.Vault).Entry);
        }
        finally { release.TrySetResult(); }
        Assert.False(await sql);
        Assert.Equal("legitimate-old-SQL", h.Guest.Settings.MyGroupId);
        Assert.Empty(h.Guest.Toasts.Items);
    }
}
