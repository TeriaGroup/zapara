using System.Net.Http.Headers;
using System.Text.Json;

namespace Vograph.Core.Services;

public class AutoUpdateService : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private bool _disposed;
    private const string Owner = "TeriaGroup";
    private const string Repo = "zapara-releases";

    public AutoUpdateService() : this(new HttpClient()) { _ownsClient = true; }

    public AutoUpdateService(HttpClient client)
    {
        _http = client ?? throw new ArgumentNullException(nameof(client));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Zapara-AutoUpdate/1.0");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <param name="ZipName">Asset name of the archive, as listed in SHA256SUMS.</param>
    /// <param name="ChecksumsUrl">The release's SHA256SUMS asset, if any.</param>
    /// <param name="SignatureUrl">The release's SHA256SUMS.sig asset, if any.</param>
    public record UpdateInfo(string Tag, string HtmlUrl, string? ZipUrl, string PublishedAt,
        string? ZipName = null, string? ChecksumsUrl = null, string? SignatureUrl = null);

    public async Task<UpdateInfo?> GetLatestAsync(string channel = "windows", CancellationToken ct = default, string? repo = null, string? token = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var repoName = string.IsNullOrWhiteSpace(repo) ? Repo : repo;
        if (repoName == UpdateChannelStore.AlphaRepo && string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("github-token");
        string pfx = channel == "android" ? "android-" : "windows-";
        var url = $"https://api.github.com/repos/{Owner}/{repoName}/releases?per_page=100";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var wantZip = !string.Equals(channel, "android", StringComparison.OrdinalIgnoreCase);
        UpdateInfo? best = null;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var tag = el.GetProperty("tag_name").GetString() ?? "";
            if (!TagMatchesChannel(tag, pfx)) continue;
            var html = el.GetProperty("html_url").GetString() ?? $"https://github.com/{Owner}/{repoName}/releases/tag/{tag}";
            var published = el.TryGetProperty("published_at", out var p) ? p.GetString() ?? "" : "";
            string? sums = null, sig = null;
            var packages = new List<(string Name, string Url)>();
            if (el.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    var assetUrl = a.GetProperty("browser_download_url").GetString();
                    if (assetUrl is null) continue;
                    if (name == UpdateVerifier.ChecksumsAssetName) { sums = assetUrl; continue; }
                    if (name == UpdateVerifier.SignatureAssetName) { sig = assetUrl; continue; }
                    packages.Add((name, assetUrl));
                }
            }
            var picked = PickAsset(packages, tag, wantZip);
            if (picked is null) continue;
            var (zipName, zip) = picked.Value;
            var cand = new UpdateInfo(tag, html, zip, published, zipName, sums, sig);
            if (best == null || BetterTag(best.Tag, tag, pfx) == tag) best = cand;
        }
        return best;
    }

    /// <summary>Windows asset name carrying the release version: ZAPARA_win-x64_2.1.43.zip.</summary>
    public static string VersionedWindowsAssetName(string tag) =>
        ParseVersion(tag) is { } v ? $"ZAPARA_win-x64_{v.ToString(3)}.zip" : "ZAPARA_win-x64.zip";

    public const string LegacyWindowsAssetName = "ZAPARA_win-x64.zip";

    /// <summary>Windows: the versioned archive for this tag, then the legacy ZAPARA_win-x64.zip, then (older
    /// releases) the first zip with ZAPARA in its name or the last zip. Android: the first ZAPARA apk or the last apk.</summary>
    public static (string Name, string Url)? PickAsset(IReadOnlyList<(string Name, string Url)> assets, string tag, bool wantZip)
    {
        var ext = wantZip ? ".zip" : ".apk";
        var candidates = assets.Where(a => a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0) return null;
        if (wantZip)
        {
            var versioned = VersionedWindowsAssetName(tag);
            foreach (var preferred in new[] { versioned, LegacyWindowsAssetName })
                foreach (var a in candidates)
                    if (string.Equals(a.Name, preferred, StringComparison.OrdinalIgnoreCase)) return a;
        }
        foreach (var a in candidates)
            if (a.Name.Contains("ZAPARA", StringComparison.OrdinalIgnoreCase)) return a;
        return candidates[^1];
    }

    public static string CurrentTagWindows => "windows-v2.1.42";

    /// <summary>Download a release asset with progress (0..1, -1 if size unknown).</summary>
    public async Task DownloadAssetAsync(string url, string destPath, IProgress<double>? progress = null, CancellationToken ct = default, string? token = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? ".");
        string tmp = destPath + ".part";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1;
            using var src = await resp.Content.ReadAsStreamAsync(ct);
            using var dst = File.Create(tmp);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
            {
                await dst.WriteAsync(buf, 0, n, ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
            dst.Close();
            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tmp, destPath);
            progress?.Report(1.0);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }
    }

    /// <summary>Download a small release asset (SHA256SUMS, its signature) into memory, refusing anything larger
    /// than <paramref name="maxBytes"/>.</summary>
    public async Task<byte[]> DownloadSmallAsync(string url, int maxBytes, CancellationToken ct = default, string? token = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("asset too large");
        using var src = await resp.Content.ReadAsStreamAsync(ct);
        using var dst = new MemoryStream();
        var buf = new byte[8192];
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            if (dst.Length + n > maxBytes) throw new InvalidDataException("asset too large");
            dst.Write(buf, 0, n);
        }
        return dst.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsClient) _http.Dispose();
    }

    public static bool TagMatchesChannel(string tag, string prefix) =>
        tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
        (tag.Length > 1 && (tag[0] == 'v' || tag[0] == 'V') && char.IsDigit(tag[1]));

    static string BetterTag(string current, string candidate, string prefix)
    {
        if (IsNewer(candidate, current)) return candidate;
        if (IsNewer(current, candidate)) return current;
        var curPfx = current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        var candPfx = candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        return candPfx && !curPfx ? candidate : current;
    }

    /// <summary>The version a release tag or a bare version string names («windows-v2.1.43», «v2.1.43», «2.1.43»),
    /// padded to four components so 2.1.43 equals 2.1.43.0. Null when it is not a version.</summary>
    public static Version? ParseVersion(string? tagOrVersion)
    {
        if (string.IsNullOrWhiteSpace(tagOrVersion)) return null;
        var t = tagOrVersion.Trim();
        t = t.Contains("-v") ? t[(t.IndexOf("-v") + 2)..] : t.Contains('-') ? t[(t.IndexOf('-') + 1)..] : t;
        if (!Version.TryParse(t.TrimStart('v', 'V'), out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }

    public static bool IsNewer(string latestTag, string currentTag)
    {
        static string ver(string t) => t.Contains("-v") ? t[(t.IndexOf("-v")+2)..] : t.Contains("-") ? t[(t.IndexOf("-")+1)..] : t;
        try
        {
            var a = new Version(ver(latestTag).TrimStart('v','V'));
            var b = new Version(ver(currentTag).TrimStart('v','V'));
            return a > b;
        }
        catch { return !string.Equals(latestTag, currentTag, StringComparison.OrdinalIgnoreCase); }
    }
}
