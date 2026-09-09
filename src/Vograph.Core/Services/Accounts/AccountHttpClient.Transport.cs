using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zapara.Contracts.Accounts;

namespace Vograph.Core.Services.Accounts;

public sealed partial class AccountHttpClient
{
    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? access, int status, CancellationToken caller)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30), clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(caller, timeout.Token);
        var ct = deadline.Token;
        byte[]? sent = null;
        byte[]? received = null;
        using var request = new HttpRequestMessage(method, new Uri(Scope.BaseUri, "api/v1/" + path));
        try
        {
            ct.ThrowIfCancellationRequested();
            if (http.DefaultRequestHeaders.Any()) throw new AccountClientException(AccountClientFailure.InvalidRequest);
            request.Headers.Accept.Add(new("application/json"));
            if (access is not null) request.Headers.Authorization = new("Bearer", access);
            if (body is not null)
            {
                sent = JsonSerializer.SerializeToUtf8Bytes(body, AccountJson.CreateOptions());
                if (sent.Length > 16384) throw new AccountClientException(AccountClientFailure.InvalidRequest);
                request.Content = new ByteArrayContent(sent);
                request.Content.Headers.ContentType = new("application/json");
            }
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var actual = (int)response.StatusCode;
            received = await ReadAsync(response.Content, actual == status ? 65536 : 4096, ct).ConfigureAwait(false);
            if (actual != status) throw Error(response, received);
            if (status == 204)
            {
                if (received.Length != 0) throw new AccountClientException(AccountClientFailure.InvalidPayload);
                return default!;
            }
            if (response.Content.Headers.ContentType?.MediaType != "application/json")
                throw new AccountClientException(AccountClientFailure.InvalidPayload);
            return AccountResponseReader.Read<T>(received);
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
            if (sent is not null) CryptographicOperations.ZeroMemory(sent);
            if (received is not null) CryptographicOperations.ZeroMemory(received);
        }
    }

    private async Task<AccountExportDownload> GetFileAsync(string path, string access, CancellationToken caller)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30), clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(caller, timeout.Token);
        var ct = deadline.Token;
        byte[]? received = null;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Scope.BaseUri, "api/v1/" + path));
        try
        {
            ct.ThrowIfCancellationRequested();
            if (http.DefaultRequestHeaders.Any()) throw new AccountClientException(AccountClientFailure.InvalidRequest);
            request.Headers.Accept.Add(new("application/json"));
            request.Headers.Authorization = new("Bearer", access);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var actual = (int)response.StatusCode;
            received = await ReadAsync(response.Content, actual == 200 ? 65536 : 4096, ct).ConfigureAwait(false);
            if (actual != 200) throw Error(response, received);
            if (response.Content.Headers.ContentType?.MediaType != "application/json")
                throw new AccountClientException(AccountClientFailure.InvalidPayload);
            var payload = received.ToArray();
            return new AccountExportDownload(payload, FileName(response));
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
            if (received is not null) CryptographicOperations.ZeroMemory(received);
        }
    }

    private static string FileName(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var raw = disposition?.FileNameStar ?? disposition?.FileName;
        if (string.IsNullOrEmpty(raw)) throw new AccountClientException(AccountClientFailure.InvalidPayload);
        var name = raw.Trim().Trim('"');
        if (name.Length is 0 or > 128 || name.IndexOfAny(['/', '\\', '\0', ':']) >= 0)
            throw new AccountClientException(AccountClientFailure.InvalidPayload);
        return name;
    }

    private static async Task<byte[]> ReadAsync(HttpContent content, int limit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > limit) throw new AccountClientException(AccountClientFailure.BodyTooLarge);
        if (content.Headers.ContentEncoding.Count != 0) throw new AccountClientException(AccountClientFailure.InvalidPayload);
        using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[limit + 1];
        try
        {
            var count = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (read == 0) break;
                count += read;
                if (count > limit) throw new AccountClientException(AccountClientFailure.BodyTooLarge);
            }
            _ = new UTF8Encoding(false, true).GetCharCount(buffer, 0, count);
            return buffer.AsSpan(0, count).ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    private AccountClientException Error(HttpResponseMessage response, byte[] bytes)
    {
        var status = (int)response.StatusCode;
        string? code = null;
        try
        {
            using var json = JsonDocument.Parse(bytes, new() { MaxDepth = 16 });
            AccountResponseReader.NoDuplicates(json.RootElement);
            var root = json.RootElement;
            if (root.TryGetProperty("status", out var s) && s.GetInt32() == status
                && root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String && c.GetString()?.Length <= 64)
                code = c.GetString();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { }
        var failure = (status, code) switch
        {
            (400 or 413 or 415, "invalid_request") => AccountClientFailure.InvalidRequest,
            (401, "invalid_credentials") => AccountClientFailure.InvalidCredentials,
            (401, "invalid_session") => AccountClientFailure.InvalidSession,
            (403, "invalid_external_proof") => AccountClientFailure.InvalidExternalProof,
            (409, "username_unavailable") => AccountClientFailure.UsernameUnavailable,
            (404, "session_not_found") => AccountClientFailure.SessionNotFound,
            (404, _) => AccountClientFailure.NotConfigured,
            (429, "rate_limited") => AccountClientFailure.RateLimited,
            (503, "db_unavailable") => AccountClientFailure.DbUnavailable,
            (503, "registration_unavailable") => AccountClientFailure.RegistrationUnavailable,
            (503, "provider_unavailable") => AccountClientFailure.ProviderUnavailable,
            (500, "internal_error") => AccountClientFailure.InternalError,
            _ => AccountClientFailure.ServerUnavailable
        };
        TimeSpan? retry = null;
        if (status == 429 && response.Headers.TryGetValues("Retry-After", out var values))
        {
            var items = values.Take(2).ToArray();
            if (items.Length == 1 && items[0].Length <= 64 && RetryConditionHeaderValue.TryParse(items[0], out var parsed))
            {
                var delta = parsed.Delta ?? parsed.Date - clock.GetUtcNow();
                if (delta is { } d && d >= TimeSpan.Zero) retry = d > TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : d;
            }
        }
        return new(failure, status, retry);
    }
}
