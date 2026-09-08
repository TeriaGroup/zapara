using System.Data;
using System.Net;
using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Profiles;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class ProfileRaceTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;
    [Fact]
    public async Task Newer_login_wins_and_late_A_never_activates_after_B()
    {
        await using var h = new ProfileHarness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var b = Guid.NewGuid();
        h.Handler.Send = async (_, _) =>
        {
            if (h.Handler.Calls == 1) { entered.SetResult(); await release.Task; return Json(Session()); }
            return Json(Session(2, Guid.NewGuid(), b));
        };
        var aLogin = h.Coordinator.LoginAsync("Test.User", Password, CT);
        try
        {
            await entered.Task.WaitAsync(CT);
            Assert.True((await h.Coordinator.LoginAsync("Test.User", Password, CT)).Committed);
        }
        finally { release.TrySetResult(); }
        Assert.False((await aLogin).Committed);
        Assert.Equal(b, h.Coordinator.Current.Services.Profile.UserId);
        Assert.Equal(b, ((AccountMemoryVault)h.Vault).Entry!.UserId);
    }

    [Fact]
    public async Task Same_user_login_reuses_connection_but_drains_credential_bound_work()
    {
        await using var h = new ProfileHarness();
        Assert.True((await h.Coordinator.LoginAsync("Test.User", Password, CT)).Committed);
        var a = h.Coordinator.Current.Services;
        h.Handler.Send = (_, _) => Task.FromResult(Json(Session(2, Guid.NewGuid())));
        using (var operation = a.Work.Enter())
        {
            Assert.Equal(ProfileFailure.Busy, (await h.Coordinator.LoginAsync("Test.User", Password, CT)).Snapshot.Failure);
            Assert.Equal(FamilyId, ((AccountMemoryVault)h.Vault).Entry!.FamilyId);
        }
        Assert.True((await h.Coordinator.LoginAsync("Test.User", Password, CT)).Committed);
        Assert.Same(a, h.Coordinator.Current.Services);
        Assert.Equal(ConnectionState.Open, a.Db.Connection.State);
        Assert.NotEqual(FamilyId, ((AccountMemoryVault)h.Vault).Entry!.FamilyId);
    }

    [Fact]
    public async Task Posted_notification_is_counted_before_dispatch_and_cannot_publish_after_resume()
    {
        await using var h = new ProfileHarness();
        Action? posted = null;
        h.Guest.NotificationScheduler.PostTick(a => posted = a);
        Assert.Equal(1, h.Guest.Work.Outstanding);
        try
        {
            Assert.Equal(ProfileFailure.Busy, (await h.Coordinator.LoginAsync("Test.User", Password, CT)).Snapshot.Failure);
            Assert.True(h.Guest.Work.IsAccepting);
        }
        finally { posted!(); }
        await h.Guest.Work.WhenIdleAsync(CT);
        Assert.Empty(h.Guest.Toasts.Items);
        Assert.True((await h.Coordinator.LoginAsync("Test.User", Password, CT)).Committed);
        Assert.Empty(h.Coordinator.Current.Services.Toasts.Items);
    }

    [Fact]
    public async Task Guest_remote_logout_is_not_critical_and_cannot_clear_new_B()
    {
        await using var h = new ProfileHarness();
        Assert.True((await h.Coordinator.LoginAsync("Test.User", Password, CT)).Committed);
        var remote = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var b = Guid.NewGuid();
        h.Handler.Send = async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("logout"))
            { await remote.Task; return new(HttpStatusCode.NoContent); }
            return Json(Session(2, Guid.NewGuid(), b));
        };
        try
        {
            Assert.True((await h.Coordinator.LogoutAsync(CT)).Committed);
            Assert.True(h.Coordinator.Current.Services.Profile.IsGuest);
            Assert.False(h.Coordinator.RemoteLogouts.IsCompleted);
            Assert.True((await h.Coordinator.LoginAsync("Test.User", Password, CT)).Committed);
        }
        finally { remote.SetResult(); }
        await h.Coordinator.RemoteLogouts;
        Assert.Equal(b, ((AccountMemoryVault)h.Vault).Entry!.UserId);
    }

    [Fact]
    public async Task Account_LAN_QR_export_import_are_denied_without_overwriting_guest_preference()
    {
        await using var h = new ProfileHarness();
        h.Guest.Prefs.LanSync = true;
        h.Guest.Prefs.Save();
        Assert.True((await h.Coordinator.LoginAsync("Test.User", Password, CT)).Committed);
        var account = h.Coordinator.Current.Services;
        Assert.Throws<InvalidOperationException>(account.LanSync.Start);
        var settings = new SettingsViewModel(account, h.Coordinator.Current.Shell);
        try
        {
            settings.LanSync = true;
            settings.LanSync = false;
            await settings.ToggleQrCommand.ExecuteAsync(null);
            await settings.ExportCommand.ExecuteAsync(null);
            await settings.ImportCommand.ExecuteAsync(null);
            Assert.False(account.LanSync.IsRunning);
            Assert.True(account.Prefs.LanSync);
            Assert.Null(settings.QrImage);
            Assert.False(File.Exists(Path.Combine(account.DataDir, "sync-qr.png")));
        }
        finally { settings.Detach(); }
    }

    [Fact]
    public async Task Exit_closes_current_connection_not_captured_guest()
    {
        await using var h = new ProfileHarness();
        Assert.True((await h.Coordinator.LoginAsync("Test.User", Password, CT)).Committed);
        var active = h.Coordinator.Current.Services;
        await h.Coordinator.ExitAsync();
        Assert.True(h.Guest.IsClosed);
        Assert.True(active.IsClosed);
        Assert.Equal(ConnectionState.Closed, active.Db.Connection.State);
        Assert.Equal(ProfilePhase.Closed, h.Coordinator.Snapshot.Phase);
    }
}
