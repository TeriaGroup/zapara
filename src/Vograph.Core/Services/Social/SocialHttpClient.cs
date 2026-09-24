using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Social;

namespace Vograph.Core.Services.Social;

public sealed class SocialClientException(int status) : Exception("Операция чата не выполнена.")
{
    public int Status { get; } = status;
}

/// <summary>Account-scoped social API. Tokens are attached only to individual requests.</summary>
public sealed class SocialHttpClient : IDisposable
{
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    public AccountServerScope Scope { get; }

    public SocialHttpClient(HttpClient http, Uri baseUri) : this(http, baseUri, false) { }

    private SocialHttpClient(HttpClient http, Uri baseUri, bool ownsHttp)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (http.DefaultRequestHeaders.Any()) throw new ArgumentException("Требуется отдельный HTTP-клиент чатов.");
        this.http = http;
        this.ownsHttp = ownsHttp;
        Scope = new(baseUri);
    }

    public static SocialHttpClient CreateOwned(Uri baseUri)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false, Credentials = null, DefaultProxyCredentials = null, MaxConnectionsPerServer = 4
        };
        return new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, baseUri, true);
    }

    public Task<SocialHomeResponse> HomeAsync(string token, CancellationToken ct = default)
        => SendAsync<SocialHomeResponse>(HttpMethod.Get, "home", token, null, 200, ct);
    public Task<SocialHomeResponse> InviteAsync(string token, string code, CancellationToken ct = default)
        => SendAsync<SocialHomeResponse>(HttpMethod.Post, "invites", token, new SocialInviteRequest(code), 200, ct);
    public Task<SocialHomeResponse> AcceptAsync(string token, Guid friendshipId, CancellationToken ct = default)
        => SendAsync<SocialHomeResponse>(HttpMethod.Post, $"invites/{Id(friendshipId)}/accept", token, null, 200, ct);
    public Task<SocialHomeResponse> DeclineAsync(string token, Guid friendshipId, CancellationToken ct = default)
        => SendAsync<SocialHomeResponse>(HttpMethod.Post, $"invites/{Id(friendshipId)}/decline", token, null, 200, ct);
    public Task<SocialPageResponse> MessagesAsync(string token, Guid conversationId, Guid? before = null, CancellationToken ct = default)
        => SendAsync<SocialPageResponse>(HttpMethod.Get, $"conversations/{Id(conversationId)}/messages" + (before is Guid id ? $"?before={Id(id)}" : ""), token, null, 200, ct);
    public Task<SocialMessageResponse> SendTextAsync(string token, Guid conversationId, string body, Guid? replyTo = null, CancellationToken ct = default)
        => SendAsync<SocialMessageResponse>(HttpMethod.Post, $"conversations/{Id(conversationId)}/messages", token, new SocialTextRequest(body, replyTo), 201, ct);
    public Task<SocialMessageResponse> EditAsync(string token, Guid conversationId, Guid messageId, string body, CancellationToken ct = default)
        => SendAsync<SocialMessageResponse>(HttpMethod.Post, $"conversations/{Id(conversationId)}/messages/{Id(messageId)}/edit", token, new SocialTextRequest(body), 200, ct);
    public Task<SocialMessageResponse> DeleteAsync(string token, Guid conversationId, Guid messageId, CancellationToken ct = default)
        => SendAsync<SocialMessageResponse>(HttpMethod.Post, $"conversations/{Id(conversationId)}/messages/{Id(messageId)}/delete", token, null, 200, ct);
    public Task<SocialMessageResponse> ReactAsync(string token, Guid conversationId, Guid messageId, string emoji, CancellationToken ct = default)
        => SendAsync<SocialMessageResponse>(HttpMethod.Post, $"conversations/{Id(conversationId)}/messages/{Id(messageId)}/reaction", token, new SocialReactionRequest(emoji), 200, ct);

    public async Task<SocialMessageResponse> SendMediaAsync(string token, Guid conversationId, string kind,
        string fileName, byte[] bytes, Guid? replyTo = null, CancellationToken ct = default, int? durationMs = null)
    {
        token = AccountValidation.Token(token, "za_");
        var limit = kind switch { "image" => 25 * 1024 * 1024, "file" => 20 * 1024 * 1024,
            "voice" => 2 * 1024 * 1024, "circle" => 8 * 1024 * 1024, _ => 0 };
        var maxDuration = kind switch { "voice" => 180_000, "circle" => 60_000, _ => 0 };
        if (limit == 0 || bytes is null || bytes.Length is 0 || bytes.Length > limit || string.IsNullOrWhiteSpace(fileName)
            || (maxDuration == 0 && durationMs is not null)
            || (durationMs is int duration && (duration < 1 || duration > maxDuration)))
            throw new SocialClientException(400);
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name)) throw new SocialClientException(400);
        var route = kind switch { "image" => "images", "file" => "files", "voice" => "voice", _ => "circles" };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(Scope.BaseUri, $"api/v2/social/conversations/{Id(conversationId)}/{route}"));
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new(kind switch { "voice" => "audio/mp4", "circle" => "video/mp4", _ => "application/octet-stream" });
        form.Add(file, "file", name);
        if (replyTo is Guid parent) form.Add(new StringContent(Id(parent)), "replyTo");
        if (durationMs is int ms) form.Add(new StringContent(ms.ToString(System.Globalization.CultureInfo.InvariantCulture)), "durationMs");
        request.Content = form;
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if ((int)response.StatusCode != 201) throw new SocialClientException((int)response.StatusCode);
            if (response.Content.Headers.ContentType?.MediaType != "application/json"
                || response.Content.Headers.ContentLength > 1024 * 1024) throw new SocialClientException(0);
            var received = await response.Content.ReadAsByteArrayAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (received.Length > 1024 * 1024) throw new SocialClientException(0);
                return JsonSerializer.Deserialize<SocialMessageResponse>(received, AccountJson.CreateOptions())
                    ?? throw new SocialClientException(0);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException) { throw new SocialClientException(0); }
            finally { CryptographicOperations.ZeroMemory(received); }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SocialClientException(0); }
        catch (HttpRequestException) { throw new SocialClientException(0); }
        finally { request.Headers.Authorization = null; }
    }

    public async Task<byte[]> ReadAttachmentAsync(string token, Guid attachmentId, CancellationToken ct = default)
    {
        token = AccountValidation.Token(token, "za_");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(Scope.BaseUri, $"api/v2/social/attachments/{Id(attachmentId)}"));
        request.Headers.Authorization = new("Bearer", token);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if ((int)response.StatusCode != 200) throw new SocialClientException((int)response.StatusCode);
            const int limit = 25 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit || response.Content.Headers.ContentEncoding.Count != 0)
                throw new SocialClientException(0);
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            using var destination = new MemoryStream();
            var buffer = new byte[64 * 1024];
            try
            {
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    if (destination.Length + read > limit) throw new SocialClientException(0);
                    destination.Write(buffer, 0, read);
                }
                return destination.ToArray();
            }
            finally { CryptographicOperations.ZeroMemory(buffer); }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SocialClientException(0); }
        catch (HttpRequestException) { throw new SocialClientException(0); }
        finally { request.Headers.Authorization = null; }
    }

    private static string Id(Guid id) => id != Guid.Empty ? id.ToString("D") : throw new ArgumentException("Пустой идентификатор.");

    private async Task<T> SendAsync<T>(HttpMethod method, string path, string token, object? body, int expected, CancellationToken ct)
    {
        token = AccountValidation.Token(token, "za_");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using var request = new HttpRequestMessage(method, new Uri(Scope.BaseUri, "api/v2/social/" + path));
        byte[]? payload = null;
        try
        {
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Authorization = new("Bearer", token);
            if (body is not null)
            {
                payload = JsonSerializer.SerializeToUtf8Bytes(body, AccountJson.CreateOptions());
                request.Content = new ByteArrayContent(payload);
                request.Content.Headers.ContentType = new("application/json");
            }
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if ((int)response.StatusCode != expected) throw new SocialClientException((int)response.StatusCode);
            if (response.Content.Headers.ContentType?.MediaType != "application/json") throw new SocialClientException(0);
            if (response.Content.Headers.ContentLength > 1024 * 1024 || response.Content.Headers.ContentEncoding.Count != 0)
                throw new SocialClientException(0);
            var bytes = await response.Content.ReadAsByteArrayAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (bytes.Length > 1024 * 1024) throw new SocialClientException(0);
                return JsonSerializer.Deserialize<T>(bytes, AccountJson.CreateOptions()) ?? throw new SocialClientException(0);
            }
            catch (JsonException) { throw new SocialClientException(0); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SocialClientException(0); }
        catch (HttpRequestException) { throw new SocialClientException(0); }
        finally
        {
            request.Headers.Authorization = null;
            if (payload is not null) CryptographicOperations.ZeroMemory(payload);
        }
    }

    public void Dispose() { if (ownsHttp) http.Dispose(); }
}
