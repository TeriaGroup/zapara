using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Profiles;
using Xunit;

namespace Vograph.Desktop.Tests;

public partial class TimetableApiClientTests
{
    [Fact]
    public async Task Profile_startup_network_failure_after_commit_belongs_to_new_profile()
    {
        using var directory = new ProfileTestDirectory();
        using var timetableHttp = new HttpClient(new TransportHandler((_, _) =>
            Task.FromResult(Text("{}", System.Net.HttpStatusCode.ServiceUnavailable))));
        var app = AppServices.Create(directory.Root, () => false, "http://127.0.0.1/", uri => new(timetableHttp, uri));
        using var accountHttp = new HttpClient(new AccountClientHandler());
        using var client = new AccountHttpClient(accountHttp, new Uri("http://127.0.0.1/"));
        using var vault = new AccountMemoryVault(client.Scope.Key);
        var coordinator = ProfileCoordinatorTests.Coordinator(app, client, vault);
        try
        {
            Assert.True((await coordinator.LoginAsync("Test.User", AccountClientTestSupport.Password, TestContext.Current.CancellationToken)).Committed);
            var active = coordinator.Current;
            await active.Shell.StartAsync();
            Assert.Same(active, coordinator.Current);
            Assert.IsType<Vograph.Desktop.Features.States.ErrorStateViewModel>(active.Shell.Current);
            Assert.NotNull(active.Services.Api.LastError);
            Assert.Equal(AccountClientTestSupport.UserId, vault.Entry!.UserId);
            Assert.True(app.IsClosed);
        }
        finally { await coordinator.ExitAsync(); }
    }

    [Fact]
    public async Task Profile_delayed_API_response_is_drained_and_never_writes_or_publishes_in_new_graph()
    {
        using var directory = new ProfileTestDirectory();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timetableHttp = new HttpClient(new TransportHandler(async (_, ct) =>
        {
            entered.TrySetResult();
            await release.Task; // deliberately ignores cancellation; caller must keep owning this continuation
            return Json(Catalog());
        }));
        var app = AppServices.Create(directory.Root, () => false, "http://127.0.0.1/", uri => new(timetableHttp, uri));
        using var accountHttp = new HttpClient(new AccountClientHandler());
        using var client = new AccountHttpClient(accountHttp, new Uri("http://127.0.0.1/"));
        using var vault = new AccountMemoryVault(client.Scope.Key);
        var coordinator = ProfileCoordinatorTests.Coordinator(app, client, vault, TimeSpan.FromMilliseconds(100));
        var refreshed = coordinator.Current.Shell.RefreshScheduleAsync(true, false);
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            var result = await coordinator.LoginAsync("Test.User", AccountClientTestSupport.Password, TestContext.Current.CancellationToken);
            Assert.Equal(ProfileFailure.Busy, result.Snapshot.Failure);
            Assert.True(app.Work.Outstanding > 0);
            release.SetResult();
            Assert.False(await refreshed);
            Assert.Empty(app.Db.GetAllGroups());
            Assert.Empty(app.Toasts.Items);
            Assert.True((await coordinator.LoginAsync("Test.User", AccountClientTestSupport.Password, TestContext.Current.CancellationToken)).Committed);
            Assert.Empty(coordinator.Current.Services.Db.GetAllGroups());
            Assert.Empty(coordinator.Current.Services.Toasts.Items);
            Assert.True(app.IsClosed);
        }
        finally { release.TrySetResult(); await refreshed; await coordinator.ExitAsync(); }
    }
}
