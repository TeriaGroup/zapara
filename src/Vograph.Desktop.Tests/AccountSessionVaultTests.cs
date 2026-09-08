using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Services.Accounts;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public class AccountSessionVaultTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static AccountServerScope Scope => new(new("https://example.invalid/root"));

    [Fact]
    public async Task Real_DPAPI_roundtrip_ciphertext_only_private_ACL_and_clear_leaves_other_data()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Real Windows DPAPI test required.");
        using var dir = new AccountVaultTestDirectory();
        var guest = Path.Combine(dir.Root, "guest.db");
        await File.WriteAllTextAsync(guest, "guest-sentinel", Ct);
        var vault = new WindowsAccountSessionVault(dir.Root, Scope);
        using (var lease = await vault.AcquireAsync(Ct)) lease.Write(AccountVaultEntry.Ready(Scope.Key, Session()));
        var file = Directory.GetFiles(dir.Root, "session.dpapi", SearchOption.AllDirectories).Single();
        var bytes = await File.ReadAllBytesAsync(file, Ct);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain(Session().AccessToken, text);
        Assert.DoesNotContain(Session().RefreshToken, text);
        Assert.DoesNotContain(Password, text);
        Assert.DoesNotContain("accessToken", text);
        var acl = new DirectoryInfo(Path.GetDirectoryName(file)!).GetAccessControl();
        Assert.True(acl.AreAccessRulesProtected);
        using var identity = WindowsIdentity.GetCurrent();
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            Assert.Equal(identity.User, rule.IdentityReference);
        var second = new WindowsAccountSessionVault(dir.Root, Scope);
        using (var lease = await second.AcquireAsync(Ct))
        {
            Assert.Equal(AccountVaultEntry.Ready(Scope.Key, Session()), lease.Read());
            lease.Write(AccountVaultEntry.Ready(Scope.Key, Session(2)));
        }
        using (var lease = await vault.AcquireAsync(Ct))
        {
            Assert.Equal(Session(2), lease.Read()!.Session);
            lease.Clear();
            Assert.Null(lease.Read());
        }
        Assert.Equal("guest-sentinel", await File.ReadAllTextAsync(guest, Ct));
        Assert.Empty(Directory.GetFiles(dir.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Wrong_server_entropy_and_corruption_fail_closed_without_deleting_file()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var dir = new AccountVaultTestDirectory();
        var vault = new WindowsAccountSessionVault(dir.Root, Scope);
        using (var lease = await vault.AcquireAsync(Ct)) lease.Write(AccountVaultEntry.Ready(Scope.Key, Session()));
        var original = Path.Combine(dir.Root, Scope.Key, "session.dpapi");
        var otherScope = new AccountServerScope(new("https://example.invalid/other"));
        var other = new WindowsAccountSessionVault(dir.Root, otherScope);
        using (var lease = await other.AcquireAsync(Ct))
        {
            var copied = Path.Combine(dir.Root, otherScope.Key, "session.dpapi");
            File.Copy(original, copied);
            var error = Assert.Throws<AccountClientException>(() => lease.Read());
            Assert.Equal(AccountClientFailure.VaultUnavailable, error.Failure);
            Assert.DoesNotContain(dir.Root, error.ToString());
            Assert.True(File.Exists(copied));
        }
        await File.WriteAllBytesAsync(original, [1, 2, 3], Ct);
        using var broken = await vault.AcquireAsync(Ct);
        Assert.Throws<AccountClientException>(() => broken.Read());
        Assert.True(File.Exists(original));
    }

    [Fact]
    public async Task Actual_share_lock_is_exclusive_cancellable_bounded_and_released()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var dir = new AccountVaultTestDirectory();
        IAccountSessionVault vault = new WindowsAccountSessionVault(dir.Root, Scope, TimeSpan.FromMilliseconds(150));
        using (var lease = await vault.AcquireAsync(Ct))
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            cancel.CancelAfter(30);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vault.AcquireAsync(cancel.Token));
            var error = await Assert.ThrowsAsync<AccountClientException>(() => vault.AcquireAsync(Ct));
            Assert.Equal(AccountClientFailure.LockTimeout, error.Failure);
        }
        using var next = await new WindowsAccountSessionVault(dir.Root, Scope).AcquireAsync(Ct);
        Assert.Null(next.Read());
    }

    [Fact]
    public async Task Actual_lock_and_DPAPI_serialize_two_vault_and_manager_instances()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var dir = new AccountVaultTestDirectory();
        var firstVault = new WindowsAccountSessionVault(dir.Root, Scope);
        var secondVault = new WindowsAccountSessionVault(dir.Root, Scope);
        using (var lease = await firstVault.AcquireAsync(Ct)) lease.Write(AccountVaultEntry.Ready(Scope.Key, Session()));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new AccountClientHandler { Send = async (_, ct) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            return Json(Session(2));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri, new AccountClientClock());
        var first = new AccountSessionManager(client, firstVault, new AccountClientClock()).RefreshIfCurrentAsync(Session(), Ct);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        var second = new AccountSessionManager(client, secondVault, new AccountClientClock()).RefreshIfCurrentAsync(Session(), Ct);
        release.SetResult();
        Assert.Equal(Session(2), await first);
        Assert.Equal(Session(2), await second);
        Assert.Equal(1, handler.Calls);
        using var persisted = await secondVault.AcquireAsync(Ct);
        Assert.Equal(Session(2), persisted.Read()!.Session);
    }

    [Fact]
    public async Task Actual_pending_survives_new_manager_without_network()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var dir = new AccountVaultTestDirectory();
        var vault = new WindowsAccountSessionVault(dir.Root, Scope);
        using (var lease = await vault.AcquireAsync(Ct))
            lease.Write(AccountVaultEntry.Ready(Scope.Key, Session()) with { RefreshState = AccountRefreshState.Pending });
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri);
        var manager = new AccountSessionManager(client, new WindowsAccountSessionVault(dir.Root, Scope));
        Assert.Equal(AccountClientFailure.ReauthenticationRequired, (await Assert.ThrowsAsync<AccountClientException>(
            () => manager.GetValidSessionAsync(Ct))).Failure);
        Assert.Equal(0, handler.Calls);
    }
}
