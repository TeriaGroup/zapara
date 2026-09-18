using System.Globalization;
using System.Net;
using Vograph.Timetable;

namespace Vograph.Desktop.Services;

public sealed record RefreshCheck(bool Modified, ParsedSchedule? Parsed);

/// <summary>
/// Network half of a live timetable refresh: GET /api/schedule/meta, then lessons for the named groups.
/// Never touches SQLite — the caller hands the snapshot to Parser.RefreshParsed under the Core gate.
/// The university XML URL is a SPA shell now; bundled XML still goes through Parser.RefreshAsync(xmlOverride).
/// </summary>
public sealed class ScheduleRefresher : IDisposable
{
    private readonly HttpClient _http;

    public ScheduleRefresher(HttpMessageHandler? handler = null, string? url = null)
    {
        _ = url;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Vograph/2.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public Task<RefreshCheck> CheckAsync(string? lastFetchedAtIso, CancellationToken ct = default)
        => CheckAsync(Array.Empty<string>(), lastFetchedAtIso, ct);

    /// <param name="groupNames">Empty: catalog only (meta, no /lessons). Selected group + friends on F5.</param>
    /// <param name="lastFetchedAtIso">Settings.LastFetchedAt (ISO 8601); null forces a full download.</param>
    public async Task<RefreshCheck> CheckAsync(IReadOnlyList<string> groupNames, string? lastFetchedAtIso, CancellationToken ct = default)
    {
        var metaJson = await GetJsonAsync(VoenmehScheduleClient.MetaUrl, ct).ConfigureAwait(false);
        var meta = VoenmehScheduleParser.ParseMeta(metaJson);
        if (lastFetchedAtIso is not null
            && TryUtc(lastFetchedAtIso, out var since)
            && TryUtc(meta.UpdatedAt, out var updated)
            && updated <= since)
            return new RefreshCheck(false, null);

        var client = new VoenmehScheduleClient(GetJsonAsync);
        var parsed = await client.FetchAsync(groupNames, meta, ct).ConfigureAwait(false);
        return new RefreshCheck(true, parsed);
    }

    private async Task<string> GetJsonAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return """{"lessons":[]}""";
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");
        return body;
    }

    private static bool TryUtc(string? iso, out DateTime utc)
    {
        if (DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            utc = dto.UtcDateTime;
            return true;
        }
        utc = default;
        return false;
    }

    public void Dispose() => _http.Dispose();
}
