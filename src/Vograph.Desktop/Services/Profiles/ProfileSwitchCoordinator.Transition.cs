using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts;

namespace Vograph.Desktop.Services.Profiles;

public sealed partial class ProfileSwitchCoordinator
{
    private async Task<ProfileSwitchResult> SwitchAsync(Request request, SessionResponse? session)
    {
        await transition.WaitAsync(request.Cancellation.Token).ConfigureAwait(false);
        var old = Current;
        var before = Snapshot;
        ProfileRoot? candidate = null;
        ExclusiveProfileLease? exclusive = null;
        var committed = false;
        var reuse = false;
        try
        {
            Check(request);
            if (retiring is not null) throw new InvalidOperationException("Сначала восстановите опубликованный профиль.");
            var profile = session is null ? ProfileDescriptor.Guest(old.Services.DataDir)
                : ProfileDescriptor.Account(old.Services.DataDir, client.Scope.Key, session.User.UserId);
            reuse = profile == old.Services.Profile;
            Snapshot = before with { Phase = ProfilePhase.Preparing, Failure = null, AccountFailure = null };
            await NotifyAsync().ConfigureAwait(false);
            candidate = reuse ? old : await PrepareAsync(profile).ConfigureAwait(false);
            Check(request);
            Snapshot = Snapshot with { Phase = ProfilePhase.Draining };
            using var deadline = new CancellationTokenSource(timeout, clock);
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, request.Cancellation.Token);
            await dispatch(old.Shell.SuspendProducers).ConfigureAwait(false);
            try { exclusive = await ExclusiveProfileLease.AcquireAsync(old.Services, bounded.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !request.Cancellation.IsCancellationRequested)
            { throw new TimeoutException("Профиль занят."); }
            Check(request);
            lock (requests)
            {
                Check(request);
                commitFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            Snapshot = Snapshot with { Phase = ProfilePhase.Committing };
            SessionResponse? logout = null;
            // From this point requests wait for commit completion. No caller cancellation may undo a durable write.
            if (session is not null) await manager.ActivateAsync(session, request.Expected, CancellationToken.None).ConfigureAwait(false);
            else logout = await ClearExpectedAsync(request.Expected).ConfigureAwait(false);
            committed = true;
            Snapshot = new(profile, session is null ? null : AccountSessionIdentity.From(session), false, ProfilePhase.Idle);
            Current = candidate;
            if (!reuse) { retiring = old; retiringLease = exclusive; exclusive = null; }
            try
            {
                await dispatch(() => { publish(Current); Current.Shell.Attach(); }).ConfigureAwait(false);
                if (!reuse) await RetireAsync().ConfigureAwait(false);
                exclusive?.Dispose();
                exclusive = null;
                if (reuse) await dispatch(Current.Shell.ResumeProducers).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Current.Services.Work.Suspend();
                Snapshot = Snapshot with { Phase = ProfilePhase.RecoveryRequired, Failure = ProfileFailure.Publication };
                Current.Services.Log.Warn("profile publication requires recovery: " + ex.GetType().Name);
            }
            if (logout is not null) StartRemoteLogout(logout);
            await NotifyAsync().ConfigureAwait(false);
            return new(true, Snapshot);
        }
        catch (Exception ex) when (!committed)
        {
            exclusive?.Dispose();
            exclusive = null;
            if (candidate is not null && !reuse) await candidate.Services.CloseCandidateAsync().ConfigureAwait(false);
            await dispatch(old.Shell.ResumeProducers).ConfigureAwait(false);
            Snapshot = before with { Failure = ex switch
            {
                TimeoutException => ProfileFailure.Busy,
                OperationCanceledException => ProfileFailure.Cancelled,
                AccountClientException => ProfileFailure.Credentials,
                _ => ProfileFailure.Preparation
            }, AccountFailure = (ex as AccountClientException)?.Failure };
            old.Services.Log.Warn("profile switch aborted: " + ex.GetType().Name);
            await NotifyAsync().ConfigureAwait(false);
            return new(false, Snapshot);
        }
        finally
        {
            exclusive?.Dispose();
            lock (requests) { commitFinished?.TrySetResult(); commitFinished = null; }
            transition.Release();
        }
    }

    private async Task<SessionResponse?> ClearExpectedAsync(AccountSessionIdentity? expected)
    {
        using var lease = await vault.AcquireAsync(CancellationToken.None).ConfigureAwait(false);
        var entry = lease.Read();
        if ((entry is null ? null : AccountSessionIdentity.From(entry.Session)) != expected)
            throw new AccountClientException(AccountClientFailure.SessionChanged);
        if (entry is not null) lease.Clear();
        return entry?.Session;
    }

    private async Task RetireAsync()
    {
        if (retiring is not { } old || retiringLease is not { } lease) return;
        await dispatch(old.Shell.Stop).ConfigureAwait(false);
        await old.Services.CloseQuiescedAsync(lease).ConfigureAwait(false);
        lease.Dispose();
        retiringLease = null;
        retiring = null;
    }

    public async Task<ProfileSnapshot> RecoverAsync(CancellationToken ct = default)
    {
        await transition.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Snapshot.Phase != ProfilePhase.RecoveryRequired) return Snapshot;
            await dispatch(() => { publish(Current); Current.Shell.Attach(); }).ConfigureAwait(false);
            await RetireAsync().ConfigureAwait(false);
            await dispatch(() => { Current.Services.Work.Resume(); Current.Shell.ResumeProducers(); }).ConfigureAwait(false);
            Snapshot = Snapshot with { Phase = ProfilePhase.Idle, Failure = null };
            await NotifyAsync().ConfigureAwait(false);
            return Snapshot;
        }
        finally { transition.Release(); }
    }
}
