using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Vograph.Core.Services.Accounts;

namespace Vograph.Desktop.Services.Accounts;

[SupportedOSPlatform("windows")]
public sealed class WindowsAccountSessionVault : IAccountSessionVault
{
    private readonly string directory;
    private readonly TimeSpan lockTimeout;
    public string ServerKey { get; }
    public WindowsAccountSessionVault(string root, AccountServerScope scope, TimeSpan? lockTimeout = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Хранилище доступно только в Windows.");
        ArgumentNullException.ThrowIfNull(scope);
        this.lockTimeout = lockTimeout ?? TimeSpan.FromSeconds(10);
        if (this.lockTimeout <= TimeSpan.Zero || this.lockTimeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(lockTimeout));
        try
        {
            if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root)) throw Failure();
            directory = Path.Combine(Path.GetFullPath(root), scope.Key);
            ServerKey = scope.Key;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { throw Failure(); }
    }

    public async Task<IAccountVaultLease> AcquireAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try { EnsurePrivateDirectory(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { throw Failure(); }
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var path = Path.Combine(directory, "session.lock");
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw Failure();
                var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var lease = new WindowsAccountVaultLease(file, directory, ServerKey);
                try { lease.CleanOrphans(); return lease; }
                catch { lease.Dispose(); throw; }
            }
            catch (IOException e) when ((e.HResult & 0xffff) is 32 or 33)
            {
                if (elapsed.Elapsed >= lockTimeout) throw new AccountClientException(AccountClientFailure.LockTimeout);
                var remaining = lockTimeout - elapsed.Elapsed;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining < TimeSpan.FromMilliseconds(25) ? remaining : TimeSpan.FromMilliseconds(25), ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { throw Failure(); }
        }
    }

    private void EnsurePrivateDirectory()
    {
        for (var parent = new DirectoryInfo(directory).Parent; parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw Failure();
        using var user = WindowsIdentity.GetCurrent();
        var sid = user.User ?? throw Failure();
        var security = new DirectorySecurity();
        security.SetOwner(sid);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var info = new DirectoryInfo(directory);
        if (!info.Exists) info.Create(security);
        info.Refresh();
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw Failure();
        var actual = info.GetAccessControl();
        if (!actual.AreAccessRulesProtected || !sid.Equals(actual.GetOwner(typeof(SecurityIdentifier)))) throw Failure();
        var rules = actual.GetAccessRules(true, true, typeof(SecurityIdentifier));
        if (rules.Count == 0) throw Failure();
        foreach (FileSystemAccessRule rule in rules)
            if (!sid.Equals(rule.IdentityReference) || rule.AccessControlType != AccessControlType.Allow) throw Failure();
    }

    private static AccountClientException Failure() => new(AccountClientFailure.VaultUnavailable);
}
