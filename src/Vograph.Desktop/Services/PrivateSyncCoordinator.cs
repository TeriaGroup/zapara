using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Sync;
using Zapara.Contracts.Sync;

namespace Vograph.Desktop.Services;

public sealed class PrivateSyncConflict
{
    public PrivateSyncConflict(string entityType, Guid entityId, string diagnostic)
    {
        EntityType = entityType;
        EntityId = entityId;
        Diagnostic = diagnostic;
    }
    public string EntityType { get; }
    public Guid EntityId { get; }
    public string Diagnostic { get; }
}

public sealed class PrivateSyncCoordinator : IDisposable
{
    private readonly AppServices app;
    private PrivateSyncHttpClient? client;
    private Func<CancellationToken, Task<string>>? access;
    private CancellationTokenSource? background;
    private Task? loop;
    private bool disposed;

    public PrivateSyncCoordinator(AppServices app)
    {
        this.app = app;
        app.Outbox.Changed += Signal;
    }

    public event Action<PrivateSyncConflict>? Conflict;
    public int AppliedCallbacks { get; private set; }
    public int IgnoredCallbacks { get; private set; }
    public bool IsAttached => client is not null && access is not null;
    public bool IsBackgroundRunning => background is not null;
    public Uri? AttachedBaseUri => client?.Scope.BaseUri;

    public void Attach(PrivateSyncHttpClient http, Func<CancellationToken, Task<string>> accessToken, bool background = false)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(accessToken);
        if (IsAttached) return;
        client = http;
        access = accessToken;
        if (background) Start();
    }

    public void Start()
    {
        if (background is not null) return;
        background = new CancellationTokenSource();
        loop = Task.Run(() => LoopAsync(background.Token));
    }

    private void Signal()
    {
        // Background loop wakes on the next wait; tests call PushPendingAsync directly.
    }

    public async Task PushPendingAsync(CancellationToken ct = default)
    {
        using var work = app.Work.Enter();
        if (!work.IsCurrent || client is null || access is null) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, work.Token);
        try
        {
            await PushBodyAsync(work, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            IgnoredCallbacks++;
        }
    }

    private async Task PushBodyAsync(Profiles.ProfileWorkLifetime.Work work, CancellationToken token)
    {
        List<PrivateSyncOutboxEntry> pending;
        await app.CoreGate.WaitAsync(token).ConfigureAwait(false);
        try { pending = app.Outbox.Pending().Where(p => p.Status == "pending").ToList(); }
        finally { app.CoreGate.Release(); }
        if (pending.Count == 0) return;

        var epoch = await EnsureEpochAsync(work, token).ConfigureAwait(false);
        if (epoch is null) return;

        foreach (var row in pending)
        {
            if (!work.IsCurrent) { IgnoredCallbacks++; return; }
            SyncMutation? mutation = null;
            await app.CoreGate.WaitAsync(token).ConfigureAwait(false);
            try { mutation = app.Outbox.BuildMutation(row, epoch.Value); }
            finally { app.CoreGate.Release(); }
            if (mutation is null) continue;

            string accessToken;
            try { accessToken = await access!(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!work.IsCurrent) { IgnoredCallbacks++; return; }

            var result = await client!.MutateAsync(accessToken, mutation, token).ConfigureAwait(false);
            await app.CoreGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (!work.IsCurrent)
                {
                    IgnoredCallbacks++;
                    return;
                }
                ApplyPush(row, result);
                if (result.State == PrivateSyncState.ResetRequired) return;
            }
            finally { app.CoreGate.Release(); }
        }
    }

    public async Task PullAsync(CancellationToken ct = default)
    {
        using var work = app.Work.Enter();
        if (!work.IsCurrent || client is null || access is null) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, work.Token);
        var epoch = await EnsureEpochAsync(work, linked.Token).ConfigureAwait(false);
        if (epoch is null) return;
        string token;
        try { token = await access(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!work.IsCurrent) { IgnoredCallbacks++; return; }

        long after;
        await app.CoreGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try { after = app.Outbox.AfterSequence; }
        finally { app.CoreGate.Release(); }

        var page = await client.ChangesAsync(token, epoch.Value, after, ct: linked.Token).ConfigureAwait(false);
        if (!work.IsCurrent) { IgnoredCallbacks++; return; }
        if (page.State == PrivateSyncState.ResetRequired)
        {
            await ResyncAsync(work, token, linked.Token).ConfigureAwait(false);
            return;
        }
        if (page.State != PrivateSyncState.Success || page.Value is null) return;
        await app.CoreGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!work.IsCurrent) { IgnoredCallbacks++; return; }
            foreach (var change in page.Value.Changes) ApplyRecord(change.Record);
            app.Outbox.SetEpoch(page.Value.Metadata.SyncEpoch, page.Value.NextAfterSequence);
        }
        finally { app.CoreGate.Release(); }
    }

    private async Task ResyncAsync(Profiles.ProfileWorkLifetime.Work work, string token, CancellationToken ct)
    {
        var manifest = await client!.BeginResyncAsync(token, ct).ConfigureAwait(false);
        if (!work.IsCurrent) { IgnoredCallbacks++; return; }
        if (manifest.State != PrivateSyncState.Success || manifest.Value is null) return;
        var header = manifest.Value;
        long after = 0;
        bool more;
        do
        {
            var page = await client.ReadResyncPageAsync(token, header, after, ct: ct).ConfigureAwait(false);
            if (!work.IsCurrent) { IgnoredCallbacks++; return; }
            if (page.State != PrivateSyncState.Success || page.Value is null) return;
            await app.CoreGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (!work.IsCurrent) { IgnoredCallbacks++; return; }
                foreach (var item in page.Value.Items) ApplyRecord(item.Record);
            }
            finally { app.CoreGate.Release(); }
            after = page.Value.NextAfterOrdinal;
            more = page.Value.HasMore;
        } while (more);
        await app.CoreGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!work.IsCurrent) { IgnoredCallbacks++; return; }
            app.Outbox.SetEpoch(header.SyncEpoch, header.HighWater);
        }
        finally { app.CoreGate.Release(); }
    }

    private void ApplyPush(PrivateSyncOutboxEntry row, PrivateSyncResult<SyncMutationResult> result)
    {
        switch (result.State)
        {
            case PrivateSyncState.Success when result.Value?.ServerRecord is { } record:
                app.Outbox.ApplyAck(row, record);
                AppliedCallbacks++;
                break;
            case PrivateSyncState.Conflict:
                app.Outbox.MarkConflict(row, result.MutationOutcome);
                Conflict?.Invoke(new PrivateSyncConflict(row.EntityType, row.EntityId, result.Diagnostic));
                break;
            case PrivateSyncState.ResetRequired:
                app.Outbox.ClearRowEpochs();
                break;
            case PrivateSyncState.Cancelled:
                IgnoredCallbacks++;
                break;
        }
    }

    private void ApplyRecord(SyncRecord record)
    {
        if (app.Outbox.HasPendingOrDraft(record.EntityType, record.EntityId)) return;
        app.Outbox.ApplyLive(record);
    }

    private async Task<Guid?> EnsureEpochAsync(Profiles.ProfileWorkLifetime.Work work, CancellationToken ct)
    {
        await app.CoreGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (app.Outbox.SyncEpoch is { } stored) return stored;
        }
        finally { app.CoreGate.Release(); }
        if (client is null || access is null) return null;
        string token;
        try { token = await access(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!work.IsCurrent) { IgnoredCallbacks++; return null; }
        var meta = await client.MetadataAsync(token, ct).ConfigureAwait(false);
        if (!work.IsCurrent) { IgnoredCallbacks++; return null; }
        if (meta.State != PrivateSyncState.Success || meta.Value is null) return null;
        await app.CoreGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!work.IsCurrent) { IgnoredCallbacks++; return null; }
            app.Outbox.SetEpoch(meta.Value.SyncEpoch, meta.Value.MinAfterSequence);
            return meta.Value.SyncEpoch;
        }
        finally { app.CoreGate.Release(); }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PushPendingAsync(ct).ConfigureAwait(false);
                await PullAsync(ct).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is AccountClientException or HttpRequestException)
            { app.Log.Warn("private sync: " + ex.GetType().Name); }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        app.Outbox.Changed -= Signal;
        background?.Cancel();
        try { loop?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        background?.Dispose();
        client?.Dispose();
        client = null;
        access = null;
    }
}
