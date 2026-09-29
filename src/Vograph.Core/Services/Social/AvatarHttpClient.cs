using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts;

namespace Vograph.Core.Services.Social;

public sealed class AvatarClientException(int status) : Exception("Не удалось загрузить фото профиля.")
{
    public int Status { get; } = status;
}

/// <summary>Small, authenticated images. A missing image is represented by null.</summary>
public sealed class AvatarHttpClient : IDisposable
{
    public const int MaxUploadBytes = 3 * 1024 * 1024;
    public const int MaxImageBytes = 512 * 1024;
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly AccountServerScope scope;
    private readonly TimeSpan requestTimeout;

    public AvatarHttpClient(HttpClient http, Uri baseUri, TimeSpan? requestTimeout = null)
        : this(http, baseUri, false, requestTimeout) { }
    private AvatarHttpClient(HttpClient http, Uri baseUri, bool ownsHttp, TimeSpan? requestTimeout = null)
    {
        if (http.DefaultRequestHeaders.Any()) throw new ArgumentException("Требуется отдельный HTTP-клиент.");
        this.http = http;
        this.ownsHttp = ownsHttp;
        scope = new AccountServerScope(baseUri);
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        if (this.requestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    public static AvatarHttpClient CreateOwned(Uri baseUri) => new(new HttpClient(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false, Credentials = null, DefaultProxyCredentials = null, MaxConnectionsPerServer = 4
    }) { Timeout = Timeout.InfiniteTimeSpan }, baseUri, true);

    public Task<byte[]?> UserAsync(string token, Guid userId, CancellationToken ct = default)
        => GetAsync(token, $"users/{Id(userId)}", ct);
    public Task<byte[]?> GroupAsync(string token, Guid communityId, CancellationToken ct = default)
        => GetAsync(token, $"groups/{Id(communityId)}", ct);
    public Task<Guid> PutMeAsync(string token, string name, byte[] bytes, CancellationToken ct = default)
        => PutAsync(token, "me", name, bytes, ct);
    public Task<Guid> PutGroupAsync(string token, Guid communityId, string name, byte[] bytes, CancellationToken ct = default)
        => PutAsync(token, $"groups/{Id(communityId)}", name, bytes, ct);
    public Task DeleteMeAsync(string token, CancellationToken ct = default) => DeleteAsync(token, "me", ct);
    public Task DeleteGroupAsync(string token, Guid communityId, CancellationToken ct = default)
        => DeleteAsync(token, $"groups/{Id(communityId)}", ct);

    private async Task<byte[]?> GetAsync(string token, string path, CancellationToken ct)
    {
        return await WithTimeout(async deadline =>
        {
            using var request = Request(HttpMethod.Get, path, token);
            request.Headers.Accept.ParseAdd("image/webp");
            using var response = await SendAsync(request, deadline).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode != HttpStatusCode.OK) throw new AvatarClientException((int)response.StatusCode);
            if (response.Content.Headers.ContentType?.MediaType != "image/webp"
                || response.Content.Headers.ContentEncoding.Count != 0
                || response.Content.Headers.ContentLength > MaxImageBytes) throw new AvatarClientException(0);
            return await ReadBoundedAsync(response.Content, MaxImageBytes, deadline).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task<Guid> PutAsync(string token, string path, string name, byte[] bytes, CancellationToken ct)
    {
        if (bytes is null || bytes.Length is 0 or > MaxUploadBytes || string.IsNullOrWhiteSpace(name))
            throw new AvatarClientException(400);
        var fileName = Path.GetFileName(name.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName)) throw new AvatarClientException(400);
        return await WithTimeout(async deadline =>
        {
            using var request = Request(HttpMethod.Put, path, token);
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new("application/octet-stream");
            form.Add(file, "file", fileName);
            request.Content = form;
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await SendAsync(request, deadline).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) throw new AvatarClientException((int)response.StatusCode);
            if (response.Content.Headers.ContentType?.MediaType != "application/json"
                || response.Content.Headers.ContentEncoding.Count != 0) throw new AvatarClientException(0);
            var received = await ReadBoundedAsync(response.Content, 4096, deadline).ConfigureAwait(false);
            try
            {
                using var json = JsonDocument.Parse(received);
                if (!json.RootElement.TryGetProperty("revision", out var revision)
                    || !Guid.TryParse(revision.GetString(), out var id) || id == Guid.Empty) throw new AvatarClientException(0);
                return id;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new AvatarClientException(0); }
            finally { CryptographicOperations.ZeroMemory(received); }
        }, ct).ConfigureAwait(false);
    }

    private async Task DeleteAsync(string token, string path, CancellationToken ct)
    {
        await WithTimeout(async deadline =>
        {
            using var request = Request(HttpMethod.Delete, path, token);
            using var response = await SendAsync(request, deadline).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.NoContent) throw new AvatarClientException((int)response.StatusCode);
            return true;
        }, ct).ConfigureAwait(false);
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, new Uri(scope.BaseUri, "api/v2/social/avatars/" + path));
        request.Headers.Authorization = new("Bearer", AccountValidation.Token(token, "za_"));
        return request;
    }

    private async Task<T> WithTimeout<T>(Func<CancellationToken, Task<T>> action, CancellationToken caller)
    {
        using var timeout = new CancellationTokenSource(requestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, timeout.Token);
        try { return await action(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested) { throw new AvatarClientException(0); }
        catch (HttpRequestException) { throw new AvatarClientException(0); }
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > limit) throw new AvatarClientException(0);
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var data = new MemoryStream();
        var buffer = new byte[8192];
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
            {
                if (data.Length + read > limit) throw new AvatarClientException(0);
                data.Write(buffer, 0, read);
            }
            return data.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    private static string Id(Guid id) => id != Guid.Empty ? id.ToString("D") : throw new ArgumentException("Пустой идентификатор.");
    public void Dispose() { if (ownsHttp) http.Dispose(); }
}
