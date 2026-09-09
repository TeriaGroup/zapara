using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zapara.Contracts.Communities;

namespace Vograph.Core.Services.Communities;

public sealed partial class CommunityHttpClient
{
    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string access, int status, CancellationToken caller)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30), clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(caller, timeout.Token);
        var ct = deadline.Token;
        byte[]? sent = null;
        byte[]? received = null;
        using var request = new HttpRequestMessage(method, new Uri(Scope.BaseUri, "api/v1/communities" + path));
        try
        {
            ct.ThrowIfCancellationRequested();
            if (http.DefaultRequestHeaders.Any()) throw new CommunityClientException(CommunityClientFailure.InvalidRequest);
            request.Headers.Accept.Add(new("application/json"));
            request.Headers.Authorization = new("Bearer", access);
            if (body is not null)
            {
                sent = JsonSerializer.SerializeToUtf8Bytes(body, CommunityJson.CreateOptions());
                if (sent.Length > CommunityValidation.RequestBytes) throw new CommunityClientException(CommunityClientFailure.InvalidRequest);
                request.Content = new ByteArrayContent(sent);
                request.Content.Headers.ContentType = new("application/json");
            }
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var actual = (int)response.StatusCode;
            received = await ReadAsync(response.Content, actual == status ? CommunityValidation.RequestBytes : 4096, ct).ConfigureAwait(false);
            if (actual != status) throw Error(response, received);
            if (response.Content.Headers.ContentType?.MediaType != "application/json")
                throw new CommunityClientException(CommunityClientFailure.InvalidPayload);
            return CommunityResponseReader.Read<T>(received);
        }
        catch (OperationCanceledException)
        {
            if (caller.IsCancellationRequested) throw new OperationCanceledException("Операция отменена.", caller);
            throw new CommunityClientException(CommunityClientFailure.Timeout);
        }
        catch (HttpRequestException) { throw new CommunityClientException(CommunityClientFailure.Transport); }
        catch (IOException) { throw new CommunityClientException(CommunityClientFailure.Transport); }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or FormatException)
        { throw new CommunityClientException(CommunityClientFailure.InvalidPayload); }
        finally
        {
            request.Headers.Authorization = null;
            if (sent is not null) CryptographicOperations.ZeroMemory(sent);
            if (received is not null) CryptographicOperations.ZeroMemory(received);
        }
    }

    private static async Task<byte[]> ReadAsync(HttpContent content, int limit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > limit) throw new CommunityClientException(CommunityClientFailure.BodyTooLarge);
        if (content.Headers.ContentEncoding.Count != 0) throw new CommunityClientException(CommunityClientFailure.InvalidPayload);
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
                if (count > limit) throw new CommunityClientException(CommunityClientFailure.BodyTooLarge);
            }
            _ = new UTF8Encoding(false, true).GetCharCount(buffer, 0, count);
            return buffer.AsSpan(0, count).ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    private CommunityClientException Error(HttpResponseMessage response, byte[] bytes)
    {
        var status = (int)response.StatusCode;
        string? code = null;
        try
        {
            var error = CommunityJson.Parse<CommunityError>(bytes);
            if (error.Status == status) code = error.Code;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or FormatException) { }
        var failure = (status, code) switch
        {
            (400 or 415, "invalid_request") => CommunityClientFailure.InvalidRequest,
            (401, "invalid_session") => CommunityClientFailure.InvalidSession,
            (403, "forbidden") => CommunityClientFailure.Forbidden,
            (404, "not_found") => CommunityClientFailure.NotFound,
            (409, "revision_conflict") => CommunityClientFailure.RevisionConflict,
            (409, "already_voted") => CommunityClientFailure.AlreadyVoted,
            (409, "already_member") => CommunityClientFailure.AlreadyMember,
            (409, "already_requested") => CommunityClientFailure.AlreadyRequested,
            (409, "poll_closed") => CommunityClientFailure.PollClosed,
            (413, "payload_too_large") => CommunityClientFailure.PayloadTooLarge,
            (429, "rate_limited") => CommunityClientFailure.RateLimited,
            (503, "db_unavailable") => CommunityClientFailure.DbUnavailable,
            (500, "internal_error") => CommunityClientFailure.InternalError,
            _ => CommunityClientFailure.ServerUnavailable
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
