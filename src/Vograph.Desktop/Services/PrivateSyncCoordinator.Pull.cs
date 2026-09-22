using Vograph.Core.Services.Sync;
using Vograph.Desktop.Services.Profiles;

namespace Vograph.Desktop.Services;

public sealed partial class PrivateSyncCoordinator
{
    public event Action? ConflictsChanged;
    public async Task<bool> ResolveAsync(SyncConflictDecision decision, Guid epoch, CancellationToken ct = default)
    {
        using var work = app.Work.Enter();
        if (!work.IsCurrent) return false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, work.Token);
        await syncGate.WaitAsync(linked.Token).ConfigureAwait(false);
        var changed = false;
        try
        {
            await app.CoreGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                changed = app.Outbox.ResolveConflict(decision, epoch, () => { linked.Token.ThrowIfCancellationRequested(); work.ThrowIfStale(); });
                return changed;
            }
            finally { app.CoreGate.Release(); }
        }
        catch (OperationCanceledException) when (!work.IsCurrent) { return false; }
        finally
        {
            syncGate.Release();
            if (work.IsCurrent) { if (changed) Applied?.Invoke(); ConflictsChanged?.Invoke(); }
        }
    }

    public async Task PullAsync(CancellationToken ct = default)
    {
        using var work = app.Work.Enter();
        if (!work.IsCurrent || client is null || access is null) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, work.Token);
        var entered = false;
        var changed = false;
        try
        {
            await syncGate.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            var token = await access(linked.Token).ConfigureAwait(false);
            var restarts = 0;
            while (true)
            {
                Check();
                var needs = await Locked(() => !app.Outbox.SnapshotReady || app.Outbox.SnapshotManifest is not null);
                if (needs)
                {
                    if (!await Resync()) return;
                    changed = true;
                }
                var cursor = await Locked(() => (Epoch: app.Outbox.SyncEpoch!.Value, After: app.Outbox.AfterSequence));
                var result = await client.ChangesAsync(token, cursor.Epoch, cursor.After, ct: linked.Token).ConfigureAwait(false);
                Check();
                if (result.State == PrivateSyncState.ResetRequired)
                {
                    await Locked(() => { app.Outbox.RequireSnapshot(); return 0; });
                    if (restarts++ > 0) return;
                    continue;
                }
                if (result.State != PrivateSyncState.Success || result.Value is null) return;
                changed |= await Locked(() => app.Outbox.ApplyChanges(result.Value, Check));
                if (!result.Value.HasMore) return;
            }

            void Check() { linked.Token.ThrowIfCancellationRequested(); work.ThrowIfStale(); }
            async Task<T> Locked<T>(Func<T> action)
            {
                await app.CoreGate.WaitAsync(linked.Token).ConfigureAwait(false);
                try { Check(); return action(); }
                finally { app.CoreGate.Release(); }
            }
            async Task<bool> Resync()
            {
                while (true)
                {
                    var manifest = await Locked(() => app.Outbox.SnapshotManifest);
                    if (manifest is null)
                    {
                        var begin = await client.BeginResyncAsync(token, linked.Token).ConfigureAwait(false);
                        Check();
                        if (begin.State != PrivateSyncState.Success || begin.Value is null) return false;
                        manifest = begin.Value;
                        await Locked(() => { app.Outbox.BeginSnapshot(manifest); return 0; });
                    }
                    var after = await Locked(() => app.Outbox.SnapshotAfterOrdinal);
                    var expired = false;
                    while (after < manifest.ItemCount)
                    {
                        var page = await client.ReadResyncPageAsync(token, manifest, after, ct: linked.Token).ConfigureAwait(false);
                        Check();
                        if (page.State == PrivateSyncState.ManifestExpired)
                        {
                            await Locked(() => { app.Outbox.DiscardSnapshot(); return 0; });
                            expired = true;
                            break;
                        }
                        if (page.State != PrivateSyncState.Success || page.Value is null) return false;
                        await Locked(() => { app.Outbox.StageSnapshot(page.Value, Check); return 0; });
                        after = page.Value.NextAfterOrdinal;
                    }
                    if (expired) { if (restarts++ > 0) return false; continue; }
                    await Locked(() => { app.Outbox.PublishSnapshot(Check); return 0; });
                    return true;
                }
            }
        }
        catch (OperationCanceledException) when (!work.IsCurrent) { IgnoredCallbacks++; }
        finally
        {
            if (entered) syncGate.Release();
            // UI posts only after the DB/cycle gates are released and under the same profile lease.
            if (work.IsCurrent)
            {
                if (changed) Applied?.Invoke();
                // Durable conflicts remain discoverable after an empty delta or a network failure.
                // The shell presents a persistent entry; receiving data must not replace a modal.
                ConflictsChanged?.Invoke();
            }
        }
    }
}
