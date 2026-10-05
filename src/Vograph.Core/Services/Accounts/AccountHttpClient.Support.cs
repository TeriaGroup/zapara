using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vograph.Core.Services.Accounts;

public sealed partial class AccountHttpClient
{
    public Task<SupportThreadResponse[]> ListSupportAsync(string accessToken, CancellationToken ct = default)
        => SendAsync<SupportThreadResponse[]>(HttpMethod.Get, "support", null, accessToken, 200, ct);

    public Task<SupportThreadResponse> OpenSupportAsync(string accessToken, string subject, string body, CancellationToken ct = default)
        => SendAsync<SupportThreadResponse>(HttpMethod.Post, "support", new SupportOpenRequest(subject, body), accessToken, 200, ct);

    public Task<SupportThreadResponse> ContinueSupportAsync(string accessToken, Guid id, string body, CancellationToken ct = default)
        => SendAsync<SupportThreadResponse>(HttpMethod.Post, "support/" + id.ToString("D"), new SupportContinueRequest(body), accessToken, 200, ct);

    public Task<SupportThreadResponse> OpenSupportAsync(string accessToken, string subject, string body, IReadOnlyList<SupportUpload> files, CancellationToken ct = default)
        => PostReportAsync(accessToken, "support", subject, body, files, ct);

    public Task<SupportThreadResponse> ContinueSupportAsync(string accessToken, Guid id, string body, IReadOnlyList<SupportUpload> files, CancellationToken ct = default)
        => PostReportAsync(accessToken, "support/" + id.ToString("D"), null, body, files, ct);

    private async Task<SupportThreadResponse> PostReportAsync(string access, string path, string? subject, string body, IReadOnlyList<SupportUpload> files, CancellationToken caller)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60), clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(caller, timeout.Token);
        var ct = deadline.Token;
        byte[]? received = null;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Scope.BaseUri, "api/v1/" + path));
        using var form = new MultipartFormDataContent();
        try
        {
            ct.ThrowIfCancellationRequested();
            if (http.DefaultRequestHeaders.Any()) throw new AccountClientException(AccountClientFailure.InvalidRequest);
            request.Headers.Accept.Add(new("application/json"));
            request.Headers.Authorization = new("Bearer", access);
            if (subject is not null) form.Add(new StringContent(subject), "subject");
            form.Add(new StringContent(body), "body");
            long total = 0;
            foreach (var file in files)
            {
                total += file.Bytes.Length;
                if (total > 14 * 1024 * 1024) throw new AccountClientException(AccountClientFailure.BodyTooLarge);
                var content = new ByteArrayContent(file.Bytes);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
                form.Add(content, file.Kind, file.Name);
            }
            request.Content = form;
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var actual = (int)response.StatusCode;
            received = await ReadAsync(response.Content, actual == 200 ? 65536 : 4096, ct).ConfigureAwait(false);
            if (actual != 200) throw Error(response, received);
            if (response.Content.Headers.ContentType?.MediaType != "application/json")
                throw new AccountClientException(AccountClientFailure.InvalidPayload);
            return AccountResponseReader.Read<SupportThreadResponse>(received);
        }
        catch (OperationCanceledException)
        {
            if (caller.IsCancellationRequested) throw new OperationCanceledException("Операция отменена.", caller);
            throw new AccountClientException(AccountClientFailure.Timeout);
        }
        catch (HttpRequestException) { throw new AccountClientException(AccountClientFailure.Transport); }
        catch (IOException) { throw new AccountClientException(AccountClientFailure.Transport); }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or FormatException)
        { throw new AccountClientException(AccountClientFailure.InvalidPayload); }
        finally
        {
            request.Headers.Authorization = null;
            if (received is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(received);
        }
    }
}

public sealed record SupportOpenRequest(string Subject, string Body);
public sealed record SupportContinueRequest(string Body);

public sealed record SupportThreadResponse
{
    [JsonConstructor]
    public SupportThreadResponse(Guid id, string subject, IReadOnlyList<SupportLineResponse> messages)
        => (Id, Subject, Messages) = (id, subject, messages);
    public Guid Id { get; }
    public string Subject { get; }
    public IReadOnlyList<SupportLineResponse> Messages { get; }
}

public sealed record SupportUpload(string Kind, string Name, string ContentType, byte[] Bytes);

public sealed record SupportAttachmentResponse(Guid Id, string Kind, string Name);

public sealed record SupportLineResponse
{
    [JsonConstructor]
    public SupportLineResponse(string author, string body, DateTimeOffset at, IReadOnlyList<SupportAttachmentResponse>? attachments = null)
        => (Author, Body, At, Attachments) = (author, body, at, attachments ?? []);
    public string Author { get; }
    public string Body { get; }
    public DateTimeOffset At { get; }
    public IReadOnlyList<SupportAttachmentResponse> Attachments { get; }
}
