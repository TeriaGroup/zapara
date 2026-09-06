using System.Globalization;
using System.Net;
using System.Text;
using Vograph.Core.Services;

namespace Vograph.Desktop.Services;

public sealed record RefreshCheck(bool Modified, string? Xml);

/// <summary>
/// Network half of a timetable refresh: HEAD with If-Modified-Since, then GET + decode. Never touches
/// SQLite — the caller hands the XML to Parser.RefreshAsync(xmlOverride) under the Core gate. Replaces
/// Core's AutoRefreshService, whose writer ran outside the gate.
/// </summary>
public sealed class ScheduleRefresher : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _url;

    public ScheduleRefresher(HttpMessageHandler? handler = null, string url = ParserService.DefaultUrl)
    {
        _url = url;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Vograph/2.0");
    }

    /// <param name="lastFetchedAtIso">Settings.LastFetchedAt (ISO 8601); null forces a full download.</param>
    public async Task<RefreshCheck> CheckAsync(string? lastFetchedAtIso, CancellationToken ct = default)
    {
        if (DateTime.TryParse(lastFetchedAtIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var since))
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, _url);
            var sinceUtc = new DateTimeOffset(since.ToUniversalTime());
            head.Headers.IfModifiedSince = sinceUtc;
            using var resp = await _http.SendAsync(head, ct);
            if (resp.StatusCode == HttpStatusCode.NotModified) return new RefreshCheck(false, null);
            if (resp.IsSuccessStatusCode && resp.Content.Headers.LastModified is { } lm && lm <= sinceUtc) return new RefreshCheck(false, null);
            // 200 without a usable Last-Modified (a server ignoring the header): download and let the parser decide.
        }
        var bytes = await _http.GetByteArrayAsync(_url, ct);
        return new RefreshCheck(true, Decode(bytes));
    }

    /// <summary>voenmeh.ru serves the XML as UTF-16LE with a BOM; mirrors ParserService.FetchXmlAsync.</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var utf8 = Encoding.UTF8.GetString(bytes);
        return utf8.Contains('\0') ? Encoding.Unicode.GetString(bytes) : utf8;
    }

    public void Dispose() => _http.Dispose();
}
