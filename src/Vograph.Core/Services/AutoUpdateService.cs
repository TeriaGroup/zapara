using System.Net.Http.Headers;
using System.Text.Json;

namespace Vograph.Core.Services;

public class AutoUpdateService
{
    private readonly HttpClient _http;
    private const string Owner = "TeriaGroup";
    private const string Repo = "zapara";
    // tag prefix for windows
    private const string Prefix = "windows-";

    public AutoUpdateService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Zapara-AutoUpdate/1.0");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public record UpdateInfo(string Tag, string HtmlUrl, string? ZipUrl, string PublishedAt);

    public async Task<UpdateInfo?> GetLatestAsync(string channel = "windows")
    {
        string pfx = channel == "android" ? "android-" : "windows-";
        // fetch all releases, pick latest matching prefix (api/releases/latest may be android)
        var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=20";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var tag = el.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith(pfx, StringComparison.OrdinalIgnoreCase)) continue;
            var html = el.GetProperty("html_url").GetString() ?? $"https://github.com/{Owner}/{Repo}/releases/tag/{tag}";
            var published = el.TryGetProperty("published_at", out var p) ? p.GetString() ?? "" : "";
            string? zip = null;
            if (el.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                    {
                        zip = a.GetProperty("browser_download_url").GetString();
                        if (name.Contains("ZAPARA", StringComparison.OrdinalIgnoreCase)) break;
                    }
                }
            }
            return new UpdateInfo(tag, html, zip, published);
        }
        return null;
    }

    public static string CurrentTagWindows => "windows-v1.2.3";
    public static string CurrentTagAndroid => "android-v1.2.1";

    /// <summary>Download a release asset with progress (0..1, -1 if size unknown).</summary>
    public async Task DownloadAssetAsync(string url, string destPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? ".");
        string tmp = destPath + ".part";
        try
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
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

    public static string UpdatesDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vograph", "updates");

    public static bool IsNewer(string latestTag, string currentTag)
    {
        // simple semver compare after prefix: v1.0, v1.1 etc
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
