using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using Vograph.Core.Models;

namespace Vograph.Core.Services;

public sealed class TimetableApiClient : IDisposable
{
    private const int MaxBytes = 16 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private bool _ownsHttp;

    /// <summary>Injected clients must disable redirects and use controlled decompression. Caller owns them.</summary>
    public TimetableApiClient(HttpClient httpClient, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _baseUri = ValidateBaseUri(baseUri);
        _http = httpClient;
    }

    public static TimetableApiClient CreateOwned(Uri baseUri)
    {
        var validated = ValidateBaseUri(baseUri);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false, Credentials = null, MaxConnectionsPerServer = 4
        };
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        return new TimetableApiClient(http, validated) { _ownsHttp = true };
    }

    public async Task<TimetableApiSnapshot> FetchAsync(IEnumerable<string> requiredGroupIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requiredGroupIds);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        try
        {
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var id in requiredGroupIds)
            {
                ct.ThrowIfCancellationRequested();
                TimetableApiJson.Require(TimetableApiJson.ValidId(id));
                ids.Add(id);
                TimetableApiJson.Require(ids.Count <= 5000);
            }
            for (var attempt = 0; ; attempt++)
            {
                // Catalog failures themselves never qualify for the pinned-generation retry.
                using var json = await GetAsync("api/v1/groups", false, ct).ConfigureAwait(false);
                var catalog = TimetableApiReader.Catalog(json.RootElement, ct);
                var byId = catalog.Groups.ToDictionary(g => g.Id, StringComparer.Ordinal);
                if (ids.Any(id => !byId.ContainsKey(id)))
                    throw new TimetableApiException(TimetableApiFailure.UnknownRequiredGroup);
                try
                {
                    var downloaded = await DownloadAsync(catalog, ids.Select(id => byId[id]), ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    return catalog with { DownloadedGroups = downloaded };
                }
                catch (TimetableApiException e) when (e.Failure == TimetableApiFailure.SnapshotUnavailable && attempt == 0)
                {
                    ct.ThrowIfCancellationRequested();
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimetableApiException(TimetableApiFailure.Timeout);
        }
        catch (InvalidOperationException)
        {
            // JsonElement can fail lazily on invalid escaped Unicode while mapping fields.
            throw new TimetableApiException(TimetableApiFailure.InvalidPayload);
        }
    }

    private async Task<ImmutableDictionary<string, TimetableApiDownloadedGroup>> DownloadAsync(
        TimetableApiSnapshot catalog, IEnumerable<TimetableApiGroup> groups, CancellationToken ct)
    {
        using var failed = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var slots = new SemaphoreSlim(4);
        var errors = new ConcurrentQueue<TimetableApiException>();
        var rows = new ConcurrentDictionary<string, TimetableApiDownloadedGroup>(StringComparer.Ordinal);
        var tasks = groups.Select(async group =>
        {
            await slots.WaitAsync(failed.Token).ConfigureAwait(false);
            try
            {
                var escaped = group.Id is "." or ".." ? group.Id.Replace(".", "%2E") : Uri.EscapeDataString(group.Id);
                var path = $"api/v1/groups/{escaped}/timetable?snapshotId={catalog.Meta.SnapshotId:D}";
                using var json = await GetAsync(path, true, failed.Token).ConfigureAwait(false);
                rows[group.Id] = TimetableApiReader.Timetable(json.RootElement, catalog, group, failed.Token);
            }
            catch (TimetableApiException e)
            {
                errors.Enqueue(e);
                failed.Cancel();
                throw;
            }
            finally { slots.Release(); }
        }).ToArray();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch
        {
            ct.ThrowIfCancellationRequested();
            // Never let a racing snapshot-not-found hide a malformed/unavailable response.
            if (errors.TryPeek(out var first))
                throw errors.FirstOrDefault(e => e.Failure != TimetableApiFailure.SnapshotUnavailable) ?? first;
            throw;
        }
        return rows.ToImmutableDictionary(StringComparer.Ordinal);
    }

    private async Task<JsonDocument> GetAsync(string path, bool pinned, CancellationToken ct)
    {
        try
        {
            // Base is validated/canonicalized, path is internal and IDs escaped. Preserve opaque dot IDs.
            var uri = new Uri(_baseUri.AbsoluteUri + path,
                new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true });
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                if (pinned && response.StatusCode == HttpStatusCode.NotFound)
                {
                    using var error = await ReadJsonAsync(response.Content, 4096, ct).ConfigureAwait(false);
                    var root = TimetableApiJson.Object(error.RootElement);
                    if (TimetableApiJson.Integer(root, "status") == 404
                        && TimetableApiJson.Text(root, "code", 64) == "snapshot_not_found")
                        throw new TimetableApiException(TimetableApiFailure.SnapshotUnavailable);
                }
                throw new TimetableApiException(TimetableApiFailure.ServerUnavailable);
            }
            return await ReadJsonAsync(response.Content, MaxBytes, ct).ConfigureAwait(false);
        }
        catch (JsonException) { throw new TimetableApiException(TimetableApiFailure.InvalidPayload); }
        catch (DecoderFallbackException) { throw new TimetableApiException(TimetableApiFailure.InvalidPayload); }
        catch (HttpRequestException) { throw new TimetableApiException(TimetableApiFailure.Transport); }
        catch (IOException) { throw new TimetableApiException(TimetableApiFailure.Transport); }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpContent content, int limit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > limit) throw new TimetableApiException(TimetableApiFailure.BodyTooLarge);
        if (content.Headers.ContentEncoding.Count != 0) throw new TimetableApiException(TimetableApiFailure.InvalidPayload);
        using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit - (int)bytes.Length + 1)), ct)
                .ConfigureAwait(false);
            if (read == 0) break;
            if (bytes.Length + read > limit) throw new TimetableApiException(TimetableApiFailure.BodyTooLarge);
            bytes.Write(buffer, 0, read);
        }
        ct.ThrowIfCancellationRequested();
        _ = new UTF8Encoding(false, true).GetCharCount(bytes.GetBuffer(), 0, (int)bytes.Length);
        return JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, (int)bytes.Length), new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static Uri ValidateBaseUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var loopback = uri.IsAbsoluteUri && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address)));
        if (!uri.IsAbsoluteUri || (uri.Scheme != "https" && !(uri.Scheme == "http" && loopback))
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Некорректный адрес API.", nameof(uri));
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
