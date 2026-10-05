using Vograph.Desktop.Services.Profiles;
using Xunit;
using Zapara.Contracts.Accounts;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class ProfileExternalTests
{
    [Fact]
    public async Task Notification_dispatch_failure_after_commit_does_not_revoke_active_session()
    {
        ProfileHarness? harness = null;
        var injected = false;
        await using var f = harness = new ProfileHarness(dispatch: action =>
        {
            if (!injected && harness?.Guest.IsClosed == true)
            {
                injected = true;
                throw new InvalidOperationException("notification dispatch failed");
            }
            action();
            return Task.CompletedTask;
        });
        var issued = Session();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Coordinator.ExternalAsync(
            _ => Task.FromResult<SessionResponse?>(issued), true, TestContext.Current.CancellationToken));
        Assert.True(injected);
        Assert.Equal(UserId, f.Coordinator.Snapshot.Identity!.UserId);
        Assert.Equal(FamilyId, f.Coordinator.Snapshot.Identity.FamilyId);
        await f.Coordinator.RemoteLogouts;
        Assert.Equal(0, f.Handler.Calls);
    }

    [Fact]
    public async Task Logout_supersedes_pending_external_login_even_when_both_start_as_guest()
    {
        await using var f = new ProfileHarness();
        var response = new TaskCompletionSource<SessionResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = f.Coordinator.ExternalAsync(_ => response.Task, true, TestContext.Current.CancellationToken);
        Assert.True((await f.Coordinator.LogoutAsync(TestContext.Current.CancellationToken)).Committed);
        f.Handler.Send = (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        response.SetResult(Session());
        var result = await pending;
        Assert.False(result.Committed);
        Assert.Equal(ProfileFailure.Cancelled, result.Snapshot.Failure);
        Assert.True(f.Coordinator.Current.Services.Profile.IsGuest);
        await f.Coordinator.RemoteLogouts;
        Assert.Equal(1, f.Handler.Calls);
    }

    [Fact]
    public async Task Missing_login_session_cannot_switch_guest_profile()
    {
        await using var f = new ProfileHarness();
        var result = await f.Coordinator.ExternalAsync(_ => Task.FromResult<SessionResponse?>(null), true, TestContext.Current.CancellationToken);
        Assert.False(result.Committed);
        Assert.True(f.Coordinator.Current.Services.Profile.IsGuest);
    }
}
