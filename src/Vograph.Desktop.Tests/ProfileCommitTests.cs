using Vograph.Desktop.Services.Profiles;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class ProfileCommitTests
{
    [Fact]
    public async Task Cancellation_after_durable_activation_cannot_resume_guest()
    {
        using var cancel = new CancellationTokenSource();
        await using var h = new ProfileHarness(publish: _ => cancel.Cancel());
        var result = await h.Coordinator.LoginAsync("Test.User", Password, cancel.Token);
        Assert.True(result.Committed);
        Assert.Equal(UserId, h.Coordinator.Snapshot.Profile.UserId);
        Assert.True(h.Guest.IsClosed);
        Assert.False(h.Guest.Work.IsAccepting);
    }

    [Fact]
    public async Task Exit_waits_for_commit_then_closes_the_new_graph()
    {
        var publishing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var h = new ProfileHarness(publish: _ => { publishing.TrySetResult(); release.Wait(TestContext.Current.CancellationToken); });
        var login = Task.Run(() => h.Coordinator.LoginAsync("Test.User", Password, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Task? exit = null;
        try
        {
            await publishing.Task.WaitAsync(TestContext.Current.CancellationToken);
            exit = h.Coordinator.ExitAsync();
            Assert.False(exit.IsCompleted);
        }
        finally { release.Set(); }
        Assert.True((await login).Committed);
        await exit!;
        Assert.Equal(ProfilePhase.Closed, h.Coordinator.Snapshot.Phase);
        Assert.Equal(UserId, h.Coordinator.Current.Services.Profile.UserId);
        Assert.True(h.Coordinator.Current.Services.IsClosed);
    }
}
