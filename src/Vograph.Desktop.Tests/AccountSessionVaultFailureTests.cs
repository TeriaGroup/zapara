using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Services.Accounts;
using Zapara.Contracts.Accounts;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public class AccountSessionVaultFailureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static AccountServerScope Scope => new(new("https://example.invalid/root"));

    [Fact]
    public async Task Next_lease_cleans_only_owned_orphan_ciphertext_temporary_files()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var dir = new AccountVaultTestDirectory();
        var vault = new WindowsAccountSessionVault(dir.Root, Scope);
        using (var lease = await vault.AcquireAsync(Ct)) lease.Write(AccountVaultEntry.Ready(Scope.Key, Session()));
        var slot = Path.Combine(dir.Root, Scope.Key);
        var orphan = Path.Combine(slot, "write-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.Copy(Path.Combine(slot, "session.dpapi"), orphan);
        var unrelated = Path.Combine(slot, "unrelated.tmp");
        await File.WriteAllTextAsync(unrelated, "unrelated-sentinel", Ct);
        using var next = await vault.AcquireAsync(Ct);
        Assert.False(File.Exists(orphan));
        Assert.Equal("unrelated-sentinel", await File.ReadAllTextAsync(unrelated, Ct));
        Assert.Equal(Session(), next.Read()!.Session);
    }

    [Fact]
    public async Task Real_failed_READY_replace_preserves_decryptable_PENDING_and_cleans_temporary()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var dir = new AccountVaultTestDirectory();
        var vault = new WindowsAccountSessionVault(dir.Root, Scope);
        using (var lease = await vault.AcquireAsync(Ct)) lease.Write(AccountVaultEntry.Ready(Scope.Key, Session()));
        FileStream? blocking = null;
        try
        {
            using var handler = new AccountClientHandler { Send = (_, _) =>
            {
                // Open AFTER PENDING is committed, and deny the READY rename with a real Windows sharing rule.
                blocking = new FileStream(Path.Combine(dir.Root, Scope.Key, "session.dpapi"), FileMode.Open, FileAccess.Read, FileShare.Read);
                return Task.FromResult(Json(Session(2)));
            } };
            using var http = new HttpClient(handler);
            using var client = new AccountHttpClient(http, Scope.BaseUri, new AccountClientClock());
            var manager = new AccountSessionManager(client, vault, new AccountClientClock());
            Assert.Equal(AccountClientFailure.VaultUnavailable, (await Assert.ThrowsAsync<AccountClientException>(
                () => manager.RefreshIfCurrentAsync(Session(), Ct))).Failure);
            Assert.NotNull(blocking);
            blocking.Dispose();
            blocking = null;
            using var persisted = await vault.AcquireAsync(Ct);
            Assert.Equal(AccountRefreshState.Pending, persisted.Read()!.RefreshState);
            Assert.Equal(Session(), persisted.Read()!.Session);
            Assert.Empty(Directory.GetFiles(dir.Root, "*.tmp", SearchOption.AllDirectories));
            Assert.Equal(1, handler.Calls);
        }
        finally { blocking?.Dispose(); }
    }

    [Fact]
    public async Task Actual_DPAPI_envelope_is_CurrentUser_and_scope_bound_not_a_plaintext_wrapper()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var dir = new AccountVaultTestDirectory();
        var vault = new WindowsAccountSessionVault(dir.Root, Scope);
        using (var lease = await vault.AcquireAsync(Ct)) lease.Write(AccountVaultEntry.Ready(Scope.Key, Session()));
        var cipher = await File.ReadAllBytesAsync(Path.Combine(dir.Root, Scope.Key, "session.dpapi"), Ct);
        var entropy = Encoding.UTF8.GetBytes(Scope.Key);
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(cipher, entropy, DataProtectionScope.CurrentUser);
            var envelope = JsonSerializer.Deserialize<AccountVaultEntry>(plain, AccountJson.CreateOptions());
            Assert.Equal(AccountVaultEntry.Ready(Scope.Key, Session()), envelope);
            var rejected = false;
            try { ProtectedData.Unprotect(cipher, Encoding.UTF8.GetBytes("other-scope"), DataProtectionScope.CurrentUser); }
            catch (CryptographicException) { rejected = true; }
            Assert.True(rejected);
            Assert.DoesNotContain(Password, Encoding.UTF8.GetString(plain));
        }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(cipher);
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    [Fact]
    public async Task Real_vault_rejects_scope_identity_and_version_envelopes_without_overwriting_ready()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var dir = new AccountVaultTestDirectory();
        var vault = new WindowsAccountSessionVault(dir.Root, Scope);
        using var lease = await vault.AcquireAsync(Ct);
        var ready = AccountVaultEntry.Ready(Scope.Key, Session());
        lease.Write(ready);
        foreach (var invalid in new[] { ready with { ServerKey = "wrong" }, ready with { UserId = Guid.NewGuid() },
            ready with { FamilyId = Guid.NewGuid() }, ready with { Version = 2 }, ready with { RefreshState = (AccountRefreshState)20 } })
            Assert.Throws<AccountClientException>(() => lease.Write(invalid));
        Assert.Equal(ready, lease.Read());
    }
}
