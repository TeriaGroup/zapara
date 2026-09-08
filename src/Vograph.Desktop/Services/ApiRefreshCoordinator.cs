using Vograph.Core.Services;
using Vograph.Core.Models;

namespace Vograph.Desktop.Services;

public sealed class ApiRefreshCoordinator : IDisposable
{
    private readonly AppServices _app;
    private readonly TimetableApiClient? _client;
    private readonly string _sourceBase = "";
    private readonly CancellationTokenSource _stop = new();
    private readonly object _policyGate = new();
    private CancellationTokenSource _policy = new();
    internal CancellationToken LifetimeToken => _stop.Token;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private long _epoch;
    private bool _disposed;

    public ApiRefreshCoordinator(AppServices app, string? baseUrl, Func<Uri, TimetableApiClient>? factory = null)
    {
        _app = app;
        Configured = baseUrl is not null;
        app.Db.UseApiCatalog = Configured;
        if (!Configured) return;
        try
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) throw new ArgumentException();
            _client = (factory ?? TimetableApiClient.CreateOwned)(uri);
            _sourceBase = uri.AbsoluteUri.TrimEnd('/') + "/";
        }
        catch (ArgumentException)
        {
            ConfigurationError = "Некорректный адрес API расписания. Проверьте VOGRAPH_API_BASE_URL.";
        }
    }

    public bool Configured { get; }
    public string? ConfigurationError { get; }
    public string? LastError { get; private set; }
    public TimetableApiFailure? LastFailure { get; private set; }
    private bool NetworkAllowed => _app.AllowNetwork && !App.ReadOfflineSwitch(Environment.GetEnvironmentVariable);

    // SQLite callers hold CoreGate, just like the existing group card and schedule readers.
    public bool HasSelectedCache
    {
        get
        {
            var selected = _app.Db.GetSettings().MyGroupId;
            if (selected is null) return _app.Db.GetAllGroups().Count > 0;
            var metadata = new TimetableApiCache(_app.Db).Read(selected);
            return metadata?.Source == "api" || metadata?.FetchedAt is not null ||
                _app.Db.GetGroup(selected)?.LastFetchedAt is not null || _app.Db.GetAllLessonsForGroup(selected).Count > 0;
        }
    }

    public bool SourceStale
    {
        get
        {
            if (!Configured) return false;
            var cache = new TimetableApiCache(_app.Db);
            var selected = _app.Db.GetSettings().MyGroupId;
            var metadata = cache.Read(selected ?? "");
            return LastError is not null || ConfigurationError is not null || metadata?.Source != "api" ||
                metadata.SourceBase != _sourceBase || metadata.Meta?.Stale != false ||
                metadata.Meta.SnapshotId != cache.Read("")?.Meta?.SnapshotId ||
                (selected is not null && !_app.Db.GetAllGroups().Any(g => g.Id == selected));
        }
    }

    private sealed record Needs(long Epoch, string? Selected, string[] Friends, string[] Ids);
    private Needs Capture()
    {
        var selected = _app.Db.GetSettings().MyGroupId;
        var friends = _app.Db.GetFriends().Where(f => f.Enabled).OrderBy(f => f.Id).ToArray();
        var ids = selected is null ? [] : new[] { selected }.Concat(friends.Select(f =>
            _app.Db.GetAllGroups().FirstOrDefault(g => g.Name == f.GroupName)?.Id ?? "")).Distinct(StringComparer.Ordinal).Order().ToArray();
        return new(Interlocked.Read(ref _epoch), selected, friends.Select(f => $"{f.Id}:{f.GroupName}").ToArray(), ids);
    }

    private bool Matches(Needs needs)
    {
        var now = Capture();
        return now.Epoch == needs.Epoch && now.Selected == needs.Selected && now.Friends.SequenceEqual(needs.Friends) && now.Ids.SequenceEqual(needs.Ids);
    }

    public async Task<bool> RefreshAsync(bool neededOnly = false, CancellationToken ct = default)
    {
        using var operation = _app.Work.Enter();
        if (!operation.IsCurrent) return false;
        if (!Configured || !NetworkAllowed || _stop.IsCancellationRequested) return false;
        if (_client is null) { LastError = ConfigurationError; return false; }
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct, operation.Token);
        using var linked = LinkRequest(caller.Token);
        var token = linked.Token;
        var entered = false;
        try
        {
            await _refresh.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            while (NetworkAllowed)
            {
                Needs needs;
                await _app.CoreGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    operation.ThrowIfStale();
                    needs = Capture();
                    var cache = new TimetableApiCache(_app.Db);
                    if (neededOnly && cache.Read("")?.SourceBase == _sourceBase && needs.Ids.All(id =>
                        id.Length > 0 && cache.Read(id) is { Source: "api" } m && m.SourceBase == _sourceBase &&
                        m.Meta?.SnapshotId == cache.Read("")?.Meta?.SnapshotId)) return false;
                }
                finally { _app.CoreGate.Release(); }

                // Resolve previously unknown friend names from a catalog without committing partial work.
                TimetableApiSnapshot snapshot;
                try
                {
                    var ids = needs.Ids;
                    if (!NetworkAllowed) return false;
                    if (ids.Contains(""))
                    {
                        var catalog = await _client.FetchAsync([], token).ConfigureAwait(false);
                        ids = new[] { needs.Selected! }.Concat(needs.Friends.Select(f =>
                            catalog.Groups.FirstOrDefault(g => g.Name == f[(f.IndexOf(':') + 1)..])?.Id
                            ?? throw new TimetableApiException(TimetableApiFailure.UnknownRequiredGroup))).Distinct().ToArray();
                    }
                    if (!NetworkAllowed) return false;
                    snapshot = await _client.FetchAsync(ids, token).ConfigureAwait(false);
                }
                catch (TimetableApiException ex)
                {
                    await _app.CoreGate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        operation.ThrowIfStale();
                        if (!Matches(needs)) { neededOnly = false; continue; }
                        LastFailure = ex.Failure;
                    }
                    finally { _app.CoreGate.Release(); }
                    throw;
                }
                await _app.CoreGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    operation.ThrowIfStale();
                    if (!NetworkAllowed) return false;
                    if (!Matches(needs)) { neededOnly = false; continue; }
                    new TimetableApiCache(_app.Db).Apply(snapshot, _sourceBase);
                    LastError = null;
                    LastFailure = null;
                    if (needs.Selected is not null && snapshot.DownloadedGroups.ContainsKey(needs.Selected))
                    {
                        try { _app.Homework.RecomputeAllStatuses(); }
                        catch (Exception) { _app.Log.Warn("api refresh: derived homework recompute failed; raw cache committed"); }
                    }
                    return true;
                }
                finally { _app.CoreGate.Release(); }
            }
            return false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception)
        {
            if (!operation.IsCurrent) { _app.Log.Warn("api refresh: retired request failed"); return false; }
            LastError = "Не удалось обновить расписание API. Локальные данные сохранены.";
            _app.Log.Warn("api refresh: failed; last-good cache retained");
            return false;
        }
        finally { if (entered) _refresh.Release(); }
    }

    public void Invalidate() => Interlocked.Increment(ref _epoch);
    private CancellationTokenSource LinkRequest(CancellationToken ct)
    {
        lock (_policyGate)
            return CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token, _policy.Token);
    }

    internal void NetworkPolicyChanged(bool allowed)
    {
        lock (_policyGate)
        {
            Invalidate();
            if (!allowed) _policy.Cancel();
            else if (_policy.IsCancellationRequested)
            {
                // Existing linked requests retain cancellation; only new calls get the new generation.
                _policy.Dispose();
                _policy = new CancellationTokenSource();
            }
        }
    }
    public void Stop() { Invalidate(); _stop.Cancel(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _client?.Dispose();
    }
}
