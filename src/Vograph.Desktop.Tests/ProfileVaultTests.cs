using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Services.Accounts;
using Vograph.Desktop.Services.Profiles;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class ProfileVaultTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;
    private static IAccountSessionVault CreateVault(string root, AccountServerScope scope) => OperatingSystem.IsWindows()
        ? new WindowsAccountSessionVault(root, scope) : throw new PlatformNotSupportedException();

    [Theory]
    [InlineData("ready", false)]
    [InlineData("pending", true)]
    [InlineData("expired", true)]
    [InlineData("family-expired", true)]
    public async Task Real_DPAPI_startup_opens_known_account_cache_without_auth_HTTP(string state, bool reauth)
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var h = new ProfileHarness(CreateVault);
        var session = Session();
        if (state == "expired") session = new(session.User, session.FamilyId, session.AccessToken, session.RefreshToken,
            "Bearer", Now.AddMinutes(-1), Now.AddMinutes(1));
        if (state == "family-expired") session = new(session.User, session.FamilyId, session.AccessToken, session.RefreshToken,
            "Bearer", Now.AddMinutes(-2), Now.AddMinutes(-1));
        var entry = AccountVaultEntry.Ready(h.Client.Scope.Key, session);
        if (state == "pending") entry = entry with { RefreshState = AccountRefreshState.Pending };
        using (var lease = await h.Vault.AcquireAsync(CT)) lease.Write(entry);
        var profile = ProfileDescriptor.Account(h.Directory.Root, h.Client.Scope.Key, UserId);
        using (var cached = h.Guest.CreateProfile(profile)) ProfileCoordinatorTests.Seed(cached, "cached-A");
        var snapshot = await h.Coordinator.RestoreAsync(CT);
        Assert.Equal(UserId, snapshot.Profile.UserId);
        Assert.Equal(reauth, snapshot.ReauthRequired);
        Assert.Equal("cached-A", h.Coordinator.Current.Services.Settings.MyGroupId);
        Assert.Equal(0, h.Handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_DPAPI_missing_or_corrupt_vault_uses_guest_without_deleting_account_DB(bool corrupt)
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var h = new ProfileHarness(CreateVault);
        var profile = ProfileDescriptor.Account(h.Directory.Root, h.Client.Scope.Key, UserId);
        using (var cached = h.Guest.CreateProfile(profile)) ProfileCoordinatorTests.Seed(cached, "cached-A");
        if (corrupt)
        {
            using (var lease = await h.Vault.AcquireAsync(CT)) lease.Write(AccountVaultEntry.Ready(h.Client.Scope.Key, Session()));
            File.WriteAllBytes(Path.Combine(h.Directory.Root, h.Client.Scope.Key, "session.dpapi"), [1, 2, 3]);
        }
        Assert.True((await h.Coordinator.RestoreAsync(CT)).Profile.IsGuest);
        Assert.Equal(0, h.Handler.Calls);
        Assert.True(File.Exists(profile.DatabasePath));
        using var reopened = h.Guest.CreateProfile(profile);
        Assert.Equal("cached-A", reopened.Settings.MyGroupId);
    }

    [Fact]
    public async Task Real_DPAPI_write_failure_keeps_A_and_resumes_its_graph()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var h = new ProfileHarness(CreateVault);
        Assert.True((await h.Coordinator.LoginAsync("Test.User", Password, CT)).Committed);
        var a = h.Coordinator.Current;
        h.Handler.Send = (_, _) => Task.FromResult(Json(Session(2, Guid.NewGuid(), Guid.NewGuid())));
        var slot = Path.Combine(h.Directory.Root, h.Client.Scope.Key, "session.dpapi");
        using (var denyRename = new FileStream(slot, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await h.Coordinator.LoginAsync("Test.User", Password, CT);
            Assert.False(result.Committed);
            Assert.Equal(ProfileFailure.Credentials, result.Snapshot.Failure);
        }
        using (var lease = await h.Vault.AcquireAsync(CT)) Assert.Equal(UserId, lease.Read()!.UserId);
        Assert.Same(a, h.Coordinator.Current);
        Assert.True(a.Services.Work.IsAccepting);
        Assert.False(a.Shell.IsStopped);
    }
}
