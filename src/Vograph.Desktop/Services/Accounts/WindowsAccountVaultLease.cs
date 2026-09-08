using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts;

namespace Vograph.Desktop.Services.Accounts;

[SupportedOSPlatform("windows")]
internal sealed class WindowsAccountVaultLease(FileStream fileLock, string directory, string serverKey) : IAccountVaultLease
{
    private FileStream? fileLock = fileLock;
    private string PathName => Path.Combine(directory, "session.dpapi");

    public AccountVaultEntry? Read()
    {
        CheckLease();
        byte[]? cipher = null;
        byte[]? plain = null;
        var entropy = Encoding.UTF8.GetBytes(serverKey);
        try
        {
            if (!File.Exists(PathName)) return null;
            CheckFile();
            using var input = new FileStream(PathName, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is < 1 or > 131072) throw Failure();
            cipher = new byte[(int)input.Length];
            input.ReadExactly(cipher);
            plain = ProtectedData.Unprotect(cipher, entropy, DataProtectionScope.CurrentUser);
            if (plain.Length > 65536) throw Failure();
            var entry = JsonSerializer.Deserialize<AccountVaultEntry>(plain, AccountJson.CreateOptions()) ?? throw Failure();
            Validate(entry);
            return entry;
        }
        catch (Exception e) when (Unsafe(e)) { throw Failure(); }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            if (cipher is not null) CryptographicOperations.ZeroMemory(cipher);
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    public void Write(AccountVaultEntry entry)
    {
        CheckLease();
        byte[]? plain = null;
        byte[]? cipher = null;
        var entropy = Encoding.UTF8.GetBytes(serverKey);
        var temporary = Path.Combine(directory, "write-" + Guid.NewGuid().ToString("N") + ".tmp");
        var created = false;
        try
        {
            Validate(entry);
            CheckFile();
            plain = JsonSerializer.SerializeToUtf8Bytes(entry, AccountJson.CreateOptions());
            if (plain.Length > 65536) throw Failure();
            cipher = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                created = true;
                output.Write(cipher);
                output.Flush(true);
            }
            // Same-directory rename publishes complete ciphertext only. Never truncate the active slot.
            File.Move(temporary, PathName, true);
            created = false;
        }
        catch (Exception e) when (Unsafe(e)) { throw Failure(); }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            if (cipher is not null) CryptographicOperations.ZeroMemory(cipher);
            CryptographicOperations.ZeroMemory(entropy);
            if (created)
            {
                try { File.Delete(temporary); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    public void Clear()
    {
        CheckLease();
        try { CheckFile(); File.Delete(PathName); }
        catch (Exception e) when (Unsafe(e)) { throw Failure(); }
    }

    internal void CleanOrphans()
    {
        CheckLease();
        try
        {
            // Only our ciphertext staging names, only while holding the slot lock. Never delete the lock file.
            foreach (var path in Directory.EnumerateFiles(directory, "write-*.tmp"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.Length == 38 && Guid.TryParseExact(name[6..], "N", out _)
                    && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) File.Delete(path);
            }
        }
        catch (Exception e) when (Unsafe(e)) { throw Failure(); }
    }

    private void Validate(AccountVaultEntry entry)
    {
        if (entry is null || entry.Version != 1 || entry.ServerKey != serverKey || entry.Session is null
            || entry.RefreshState is not (AccountRefreshState.Ready or AccountRefreshState.Pending)
            || entry.UserId != entry.Session.User.UserId || entry.FamilyId != entry.Session.FamilyId
            || entry.Session.User.CreatedAt >= entry.Session.AccessExpiresAt) throw Failure();
    }

    private void CheckFile()
    {
        if (File.Exists(PathName) && (File.GetAttributes(PathName) & FileAttributes.ReparsePoint) != 0) throw Failure();
    }
    private void CheckLease() { if (fileLock is null) throw Failure(); }
    private static bool Unsafe(Exception e) => e is IOException or UnauthorizedAccessException or CryptographicException
        or JsonException or ArgumentException or System.Security.SecurityException or InvalidOperationException;
    private static AccountClientException Failure() => new(AccountClientFailure.VaultUnavailable);
    public void Dispose() => Interlocked.Exchange(ref fileLock, null)?.Dispose();
}
