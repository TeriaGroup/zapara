using System.Text.Json.Serialization;
using Zapara.Contracts.Accounts;

namespace Vograph.Core.Services.Accounts;

public enum AccountRefreshState { Ready, Pending }

public sealed record AccountSessionIdentity(Guid UserId, Guid FamilyId)
{
    public static AccountSessionIdentity From(SessionResponse session) => new(session.User.UserId, session.FamilyId);
}

/// <summary>Secret-bearing persistence envelope. Never log or destructure it.</summary>
public sealed record AccountVaultEntry(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string ServerKey,
    [property: JsonRequired] Guid UserId,
    [property: JsonRequired] Guid FamilyId,
    [property: JsonRequired] SessionResponse Session,
    [property: JsonRequired] AccountRefreshState RefreshState)
{
    public static AccountVaultEntry Ready(string serverKey, SessionResponse session)
        => new(1, serverKey, session.User.UserId, session.FamilyId, session, AccountRefreshState.Ready);
    public override string ToString() => "AccountVaultEntry { [REDACTED] }";
}

/// <summary>Trusted injection seam. All accesses occur inside an exclusive cross-instance lease.</summary>
public interface IAccountSessionVault
{
    string ServerKey { get; }
    Task<IAccountVaultLease> AcquireAsync(CancellationToken ct = default);
}

/// <summary>Write is atomic and durable before return; failure preserves the previous entry.
/// Dispose releases ownership, including across awaits. Clear only removes credentials.</summary>
public interface IAccountVaultLease : IDisposable
{
    AccountVaultEntry? Read();
    void Write(AccountVaultEntry entry);
    void Clear();
}

public sealed record AccountLogoutResult(bool LocalCleared, bool RemoteRevoked);
