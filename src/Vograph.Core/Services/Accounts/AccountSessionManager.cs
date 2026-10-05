using Zapara.Contracts.Accounts;

namespace Vograph.Core.Services.Accounts;

/// <summary>Credential lifecycle only. Does not switch databases or replay authorized mutations.</summary>
public sealed class AccountSessionManager
{
    private readonly AccountHttpClient client;
    private readonly IAccountSessionVault vault;
    private readonly TimeProvider clock;

    public AccountSessionManager(AccountHttpClient client, IAccountSessionVault vault, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(vault);
        if (client.Scope.Key != vault.ServerKey) throw new AccountClientException(AccountClientFailure.VaultUnavailable);
        this.client = client;
        this.vault = vault;
        this.clock = clock ?? TimeProvider.System;
    }

    public async Task<SessionResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        AccountSessionIdentity? expected;
        using (var lease = await vault.AcquireAsync(ct).ConfigureAwait(false))
        {
            var old = lease.Read();
            expected = old is null ? null : AccountSessionIdentity.From(old.Session);
        }
        var session = await client.LoginAsync(request, ct).ConfigureAwait(false);
        return await ActivateAsync(session, expected, ct).ConfigureAwait(false);
    }

    /// <summary>Compare-and-replace the slot. Caller must drain profile work separately before using the returned session.</summary>
    public async Task<SessionResponse> ActivateAsync(SessionResponse session, AccountSessionIdentity? expected, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        AccountResponseReader.ValidateSession(session);
        using var lease = await vault.AcquireAsync(ct).ConfigureAwait(false);
        var old = lease.Read();
        if (old is null ? expected is not null : expected != AccountSessionIdentity.From(old.Session)) throw Changed();
        ct.ThrowIfCancellationRequested();
        lease.Write(AccountVaultEntry.Ready(vault.ServerKey, session));
        return session;
    }

    public async Task<SessionResponse> GetValidSessionAsync(CancellationToken ct = default)
    {
        using var lease = await vault.AcquireAsync(ct).ConfigureAwait(false);
        var current = Validated(lease.Read());
        if (current.RefreshState == AccountRefreshState.Pending)
            return await ResumeAsync(lease, current, ct).ConfigureAwait(false);
        if (current.Session.AccessExpiresAt > clock.GetUtcNow().AddSeconds(30)) return current.Session;
        return await RotateAsync(lease, current, ct).ConfigureAwait(false);
    }

    public async Task<SessionResponse> RefreshIfCurrentAsync(SessionResponse observed, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(observed);
        using var lease = await vault.AcquireAsync(ct).ConfigureAwait(false);
        var entry = lease.Read();
        if (entry is null || AccountSessionIdentity.From(entry.Session) != AccountSessionIdentity.From(observed)) throw Changed();
        var current = Validated(entry);
        if (current.RefreshState == AccountRefreshState.Pending)
            return await ResumeAsync(lease, current, ct).ConfigureAwait(false);
        // Another waiter has already rotated this exact family. Never rotate a second time for its stale observation.
        if (current.Session.AccessToken != observed.AccessToken) return current.Session;
        return await RotateAsync(lease, current, ct).ConfigureAwait(false);
    }

    private async Task<SessionResponse> RotateAsync(IAccountVaultLease lease, AccountVaultEntry current, CancellationToken ct)
    {
        if (current.Session.RefreshExpiresAt <= clock.GetUtcNow()) throw Reauthenticate();
        ct.ThrowIfCancellationRequested();
        var attemptId = Guid.NewGuid();
        lease.Write(current with { RefreshState = AccountRefreshState.Pending, RefreshAttemptId = attemptId });
        return await CompleteRotationAsync(lease, current, attemptId, ct).ConfigureAwait(false);
    }

    private async Task<SessionResponse> ResumeAsync(IAccountVaultLease lease, AccountVaultEntry current, CancellationToken ct)
    {
        if (current.RefreshAttemptId is not { } attemptId) throw Reauthenticate();
        if (current.Session.RefreshExpiresAt <= clock.GetUtcNow()) throw Reauthenticate();
        var resumed = await CompleteRotationAsync(lease, current, attemptId, ct).ConfigureAwait(false);
        // An idempotent replay can return the original replacement after its access token has expired.
        if (resumed.AccessExpiresAt <= clock.GetUtcNow().AddSeconds(30))
            return await RotateAsync(lease, AccountVaultEntry.Ready(vault.ServerKey, resumed), ct).ConfigureAwait(false);
        return resumed;
    }

    private async Task<SessionResponse> CompleteRotationAsync(IAccountVaultLease lease, AccountVaultEntry current,
        Guid attemptId, CancellationToken ct)
    {
        SessionResponse next;
        try { next = await client.RefreshResumableAsync(current.Session.RefreshToken, attemptId, ct).ConfigureAwait(false); }
        catch (AccountClientException ex) when (ex.Status == 404 && ex.Failure == AccountClientFailure.NotConfigured)
        {
            // A missing v2 route is definitive. Mark the v1 attempt as non-resumable before sending it.
            ct.ThrowIfCancellationRequested();
            lease.Write(current with { RefreshState = AccountRefreshState.Pending, RefreshAttemptId = null });
            next = await client.RefreshAsync(current.Session.RefreshToken, ct).ConfigureAwait(false);
        }
        catch (AccountClientException ex) when (ex.Failure is AccountClientFailure.InvalidSession or AccountClientFailure.SessionNotFound)
        {
            // The server has definitively rejected this family; repeating the attempt cannot recover it.
            lease.Write(current with { RefreshState = AccountRefreshState.Pending, RefreshAttemptId = null });
            throw;
        }
        var old = current.Session;
        if (next.User.UserId != old.User.UserId || next.FamilyId != old.FamilyId || next.User.CreatedAt != old.User.CreatedAt
            || next.RefreshExpiresAt > old.RefreshExpiresAt
            || old.RefreshExpiresAt - next.RefreshExpiresAt > TimeSpan.FromTicks(10)
            || next.AccessToken == old.AccessToken || next.RefreshToken == old.RefreshToken)
            throw new AccountClientException(AccountClientFailure.InvalidPayload);
        ct.ThrowIfCancellationRequested();
        lease.Write(AccountVaultEntry.Ready(vault.ServerKey, next));
        return next;
    }

    public async Task<AccountLogoutResult> LogoutAsync(AccountSessionIdentity expected, CancellationToken ct = default)
    {
        SessionResponse session;
        using (var lease = await vault.AcquireAsync(ct).ConfigureAwait(false))
        {
            var entry = lease.Read();
            if (entry is null || AccountSessionIdentity.From(entry.Session) != expected) return new(false, false);
            session = entry.Session;
            ct.ThrowIfCancellationRequested();
            lease.Clear();
        }
        try
        {
            await client.LogoutAsync(session.AccessToken, ct).ConfigureAwait(false);
            return new(true, true);
        }
        catch (Exception e) when (e is AccountClientException or OperationCanceledException) { return new(true, false); }
    }

    public async Task ChangePasswordAsync(AccountSessionIdentity expected, ChangePasswordRequest request, CancellationToken ct = default)
    {
        using var lease = await vault.AcquireAsync(ct).ConfigureAwait(false);
        var entry = Ready(lease.Read());
        if (AccountSessionIdentity.From(entry.Session) != expected) throw Changed();
        await client.ChangePasswordAsync(entry.Session.AccessToken, request, ct).ConfigureAwait(false);
        // Acknowledged 204 revoked every server session. Do not let a late cancellation prevent local clearing.
        lease.Clear();
    }

    private AccountVaultEntry Validated(AccountVaultEntry? entry)
    {
        if (entry is null) throw Reauthenticate();
        if (entry.ServerKey != vault.ServerKey || entry.Version != 1 || entry.UserId != entry.Session.User.UserId
            || entry.FamilyId != entry.Session.FamilyId || entry.RefreshAttemptId == Guid.Empty
            || entry.RefreshState is not (AccountRefreshState.Ready or AccountRefreshState.Pending)
            || entry.RefreshState == AccountRefreshState.Ready && entry.RefreshAttemptId is not null)
            throw new AccountClientException(AccountClientFailure.VaultUnavailable);
        return entry;
    }
    private AccountVaultEntry Ready(AccountVaultEntry? entry)
    {
        entry = Validated(entry);
        if (entry.RefreshState != AccountRefreshState.Ready) throw Reauthenticate();
        return entry;
    }
    private static AccountClientException Reauthenticate() => new(AccountClientFailure.ReauthenticationRequired);
    private static AccountClientException Changed() => new(AccountClientFailure.SessionChanged);
}
