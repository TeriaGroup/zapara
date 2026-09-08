using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts;

namespace Vograph.Desktop.Services.Profiles;

public sealed partial class ProfileSwitchCoordinator
{
    private readonly CancellationTokenSource shutdown = new();
    private readonly List<Task> remoteLogouts = [];
    public Task RemoteLogouts => Task.WhenAll(remoteLogouts);

    /// <summary>Read a local lease, never refresh/authenticate. Cached identity is not server authorization.</summary>
    public async Task<ProfileSnapshot> RestoreAsync(CancellationToken ct = default)
    {
        await transition.WaitAsync(ct).ConfigureAwait(false);
        ProfileRoot? candidate = null;
        try
        {
            if (Snapshot.Identity is not null || !Current.Services.Profile.IsGuest) return Snapshot;
            AccountVaultEntry? entry;
            try
            {
                using var lease = await vault.AcquireAsync(ct).ConfigureAwait(false);
                entry = lease.Read();
            }
            catch (AccountClientException ex)
            {
                Snapshot = Snapshot with { ReauthRequired = true, AccountFailure = ex.Failure };
                return Snapshot;
            }
            if (entry is null) return Snapshot;
            candidate = await PrepareAsync(ProfileDescriptor.Account(Current.Services.DataDir, client.Scope.Key, entry.UserId)).ConfigureAwait(false);
            var old = Current;
            await dispatch(old.Shell.SuspendProducers).ConfigureAwait(false);
            using var bounded = new CancellationTokenSource(timeout, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, bounded.Token);
            using var exclusive = await ExclusiveProfileLease.AcquireAsync(old.Services, linked.Token).ConfigureAwait(false);
            // No HTTP and no mutation. Re-read while exclusively draining to reject an externally replaced slot.
            using var confirm = await vault.AcquireAsync(ct).ConfigureAwait(false);
            if (confirm.Read() != entry) throw new AccountClientException(AccountClientFailure.SessionChanged);
            await dispatch(() => { publish(candidate); candidate.Shell.Attach(); }).ConfigureAwait(false);
            Current = candidate;
            candidate = null;
            Snapshot = new(Current.Services.Profile, AccountSessionIdentity.From(entry.Session),
                entry.RefreshState != AccountRefreshState.Ready || entry.Session.AccessExpiresAt <= clock.GetUtcNow().AddSeconds(30)
                    || entry.Session.RefreshExpiresAt <= clock.GetUtcNow(), ProfilePhase.Idle);
            await dispatch(old.Shell.Stop).ConfigureAwait(false);
            await old.Services.CloseQuiescedAsync(exclusive).ConfigureAwait(false);
            await NotifyAsync().ConfigureAwait(false);
            return Snapshot;
        }
        catch
        {
            if (candidate is not null) await candidate.Services.CloseCandidateAsync().ConfigureAwait(false);
            await dispatch(Current.Shell.ResumeProducers).ConfigureAwait(false);
            throw;
        }
        finally { transition.Release(); }
    }

    private void StartRemoteLogout(SessionResponse session)
    {
        remoteLogouts.Add(RevokeAsync());
        async Task RevokeAsync()
        {
            try { await client.LogoutAsync(session.AccessToken, shutdown.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is AccountClientException or OperationCanceledException)
            { Current.Services.Log.Warn("remote logout not confirmed; local logout committed"); }
        }
    }

    public async Task ExitAsync()
    {
        lock (requests) { exiting = true; if (commitFinished is null && latest is { } pending) _ = pending.CancelAsync(); }
        await transition.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Snapshot.Phase == ProfilePhase.Closed) return;
            await shutdown.CancelAsync().ConfigureAwait(false);
            await dispatch(Current.Shell.SuspendProducers).ConfigureAwait(false);
            using var exclusive = await ExclusiveProfileLease.AcquireAsync(Current.Services, CancellationToken.None).ConfigureAwait(false);
            await dispatch(Current.Shell.Stop).ConfigureAwait(false);
            await Current.Services.CloseQuiescedAsync(exclusive).ConfigureAwait(false);
            await RetireAsync().ConfigureAwait(false);
            Snapshot = Snapshot with { Phase = ProfilePhase.Closed };
        }
        finally { transition.Release(); }
    }
}
