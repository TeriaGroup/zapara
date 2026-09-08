using System.Net;
using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Sync;

namespace Vograph.Core.Services.Sync;

/// <summary>Injected clients are trusted: disable redirects, cookies, decompression and default headers.</summary>
public sealed partial class PrivateSyncHttpClient : IDisposable
{
    private readonly HttpClient http;
    private readonly TimeProvider clock;
    private bool ownsHttp;
    public AccountServerScope Scope { get; }

    public PrivateSyncHttpClient(HttpClient http, Uri apiBaseUri, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        Scope = new AccountServerScope(apiBaseUri);
        if (http.DefaultRequestHeaders.Any()) throw new ArgumentException("Требуется отдельный HTTP-клиент синхронизации.");
        this.http = http;
        this.clock = clock ?? TimeProvider.System;
    }

    public static PrivateSyncHttpClient CreateOwned(Uri apiBaseUri, TimeProvider? clock = null)
    {
        var scope = new AccountServerScope(apiBaseUri);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false, Credentials = null, DefaultProxyCredentials = null, MaxConnectionsPerServer = 4
        };
        return new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, scope.BaseUri, clock) { ownsHttp = true };
    }

    public Task<PrivateSyncResult<SyncMetadata>> MetadataAsync(string access, CancellationToken ct = default)
        => SendAsync<SyncMetadata>(HttpMethod.Get, "metadata", access, null, _ => true, ct);

    public Task<PrivateSyncResult<SyncMutationResult>> MutateAsync(string access, SyncMutation request, CancellationToken ct = default)
        => request is null ? Invalid<SyncMutationResult>() : SendAsync<SyncMutationResult>(HttpMethod.Post,
            "mutations", access, request, result => Matches(result, request), ct);

    public Task<PrivateSyncResult<SyncChangesPage>> ChangesAsync(string access, Guid epoch, long afterSequence, int limit = 100, CancellationToken ct = default)
        => epoch == Guid.Empty || !ValidPage(afterSequence, limit) ? Invalid<SyncChangesPage>() :
            SendAsync<SyncChangesPage>(HttpMethod.Get,
                FormattableString.Invariant($"changes?epoch={epoch:D}&afterSequence={afterSequence}&limit={limit}"), access, null,
                page => page.Metadata.SyncEpoch == epoch && page.AfterSequence == afterSequence && page.Changes.Count <= limit, ct);

    public Task<PrivateSyncResult<SyncResyncManifest>> BeginResyncAsync(string access, CancellationToken ct = default)
        => SendAsync<SyncResyncManifest>(HttpMethod.Post, "resync", access, null, _ => true, ct);

    public Task<PrivateSyncResult<SyncResyncPage>> ReadResyncPageAsync(string access, SyncResyncManifest manifest, long afterOrdinal, int limit = 100, CancellationToken ct = default)
        => manifest is null || !ValidPage(afterOrdinal, limit) || afterOrdinal > manifest.ItemCount ? Invalid<SyncResyncPage>() :
            SendAsync<SyncResyncPage>(HttpMethod.Get,
                FormattableString.Invariant($"resync/{manifest.ManifestId:D}?afterOrdinal={afterOrdinal}&limit={limit}"), access, null,
                page => page.Manifest == manifest && page.AfterOrdinal == afterOrdinal && page.Items.Count <= limit, ct);

    private static bool ValidPage(long after, int limit) => after >= 0 && limit is >= 1 and <= SyncValidation.PageRecords;
    private static Task<PrivateSyncResult<T>> Invalid<T>() where T : class
        => Task.FromResult(new PrivateSyncResult<T>(PrivateSyncState.InvalidRequest));

    private static bool Matches(SyncMutationResult result, SyncMutation request)
    {
        // A reset deliberately announces the new epoch; the pending operation remains unchanged.
        if (result.Status == 410) return true;
        if (result.Metadata.SyncEpoch != request.SyncEpoch) return false;
        if (result.ServerRecord is not { } record) return result.Status != 200;
        if (record.EntityId != request.EntityId || record.EntityType != request.EntityType ||
            record.Revision > result.Metadata.CurrentSequence) return false;
        return result.Status != 200 || (record.Revision > request.ExpectedRevision &&
            record.Tombstone == (request.Action == "delete") && record.Value == request.Value);
    }

    public static long PullNextAfterSequence(SyncChangesPage page)
        => page is null ? throw new ArgumentNullException(nameof(page)) : page.NextAfterSequence;

    public void Dispose() { if (ownsHttp) http.Dispose(); }
}
