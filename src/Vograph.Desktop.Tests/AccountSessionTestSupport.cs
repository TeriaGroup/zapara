using Vograph.Core.Services.Accounts;
using Xunit;

namespace Vograph.Desktop.Tests;

internal sealed class AccountMemoryVault(string key) : IAccountSessionVault, IDisposable
{
    private readonly SemaphoreSlim gate = new(1);
    public string ServerKey => key;
    internal AccountVaultEntry? Entry;
    internal bool FailReady;
    internal bool FailPending;
    public async Task<IAccountVaultLease> AcquireAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        return new Lease(this);
    }
    private sealed class Lease(AccountMemoryVault owner) : IAccountVaultLease
    {
        public AccountVaultEntry? Read() => owner.Entry;
        public void Write(AccountVaultEntry entry)
        {
            if ((owner.FailReady && entry.RefreshState == AccountRefreshState.Ready)
                || (owner.FailPending && entry.RefreshState == AccountRefreshState.Pending))
                throw new AccountClientException(AccountClientFailure.VaultUnavailable);
            owner.Entry = entry;
        }
        public void Clear() => owner.Entry = null;
        public void Dispose() => owner.gate.Release();
    }
    public void Dispose() => gate.Dispose();
}

internal sealed class AccountVaultTestDirectory : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "zapara-account-test-" + Guid.NewGuid().ToString("N"));
    public AccountVaultTestDirectory() => Directory.CreateDirectory(Root);
    public void Dispose()
    {
        Directory.Delete(Root, true);
        Assert.False(Directory.Exists(Root));
    }
}
