using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Sync;

namespace Vograph.Core.Services.Sync;

public sealed partial class PrivateSyncHttpClient
{
    private async Task<PrivateSyncResult<T>> SendAsync<T>(HttpMethod method, string path, string access,
        SyncMutation? mutation, Func<T, bool> matches, CancellationToken ct) where T : class
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        var token = linked.Token;
        byte[]? body = null;
        HttpRequestMessage? request = null;
        var sending = false;
        try
        {
            token.ThrowIfCancellationRequested();
            AccountValidation.Token(access, "za_");
            if (http.DefaultRequestHeaders.Any()) return new(PrivateSyncState.InvalidRequest);
            if (mutation is not null)
            {
                body = SyncJson.Serialize(mutation);
                if (body.Length > SyncValidation.RequestBytes) return new(PrivateSyncState.InvalidRequest);
            }
            request = new(method, new Uri(Scope.BaseUri, "api/v1/sync/" + path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
            {
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            }
            sending = true;
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400) return new(PrivateSyncState.InvalidResponse);
            if (response.Content.Headers.ContentEncoding.Count != 0) return new(PrivateSyncState.InvalidResponse);
            var success = status == 200 || (mutation is not null && status is 409 or 410);
            var limit = success
                ? typeof(T) == typeof(SyncChangesPage) || typeof(T) == typeof(SyncResyncPage)
                    ? SyncValidation.PageBytes : SyncValidation.RequestBytes
                : 4096;
            var bytes = await ReadBoundedAsync(response.Content, limit, token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                if (!JsonMedia(response.Content.Headers.ContentType, !success))
                    return new(PrivateSyncState.InvalidResponse);
                var result = Decode(bytes, status, mutation is not null, matches, response);
                token.ThrowIfCancellationRequested();
                return result;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (OperationCanceledException)
        { return new(ct.IsCancellationRequested ? PrivateSyncState.Cancelled : PrivateSyncState.TimedOut); }
        catch (Exception e) when (e is HttpRequestException or IOException)
        { return new(PrivateSyncState.Unavailable); }
        catch (Exception e) when (e is ArgumentException or JsonException or InvalidOperationException
            or FormatException or DecoderFallbackException)
        { return new(sending ? PrivateSyncState.InvalidResponse : PrivateSyncState.InvalidRequest); }
        finally
        {
            // Managed strings cannot be erased; release message references and wipe owned byte buffers.
            if (request is not null)
            {
                request.Headers.Clear();
                request.Content?.Headers.Clear();
                request.Dispose();
            }
            if (body is not null) CryptographicOperations.ZeroMemory(body);
        }
    }

    private static bool JsonMedia(MediaTypeHeaderValue? media, bool allowProblem)
        => (string.Equals(media?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
            (allowProblem && string.Equals(media?.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))) &&
            (media?.CharSet is not { } charset || charset.Trim('"').Equals("utf-8", StringComparison.OrdinalIgnoreCase));

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > limit) throw new ArgumentException();
        using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[limit + 1];
        try
        {
            var total = 0;
            while (true)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (count == 0) break;
                total += count;
                if (total > limit) throw new ArgumentException();
            }
            _ = new UTF8Encoding(false, true).GetCharCount(buffer, 0, total);
            return buffer.AsSpan(0, total).ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    private PrivateSyncResult<T> Decode<T>(byte[] bytes, int status, bool mutation,
        Func<T, bool> matches, HttpResponseMessage response) where T : class
    {
        if (status == 200 || (mutation && status is 409 or 410))
        {
            var value = SyncJson.Parse<T>(bytes);
            if (!matches(value)) return new(PrivateSyncState.InvalidResponse);
            if (value is SyncMutationResult outcome)
            {
                if (outcome.Status != status) return new(PrivateSyncState.InvalidResponse);
                return new(status == 200 ? PrivateSyncState.Success : status == 409 ? PrivateSyncState.Conflict : PrivateSyncState.ResetRequired,
                    status == 200 ? value : null, outcome);
            }
            return new(PrivateSyncState.Success, value);
        }
        var error = PrivateSyncErrorReader.Parse(bytes);
        if (error.Status != status) return new(PrivateSyncState.InvalidResponse);
        var state = (status, error.Code) switch
        {
            (400, _) or (413, _) => PrivateSyncState.InvalidRequest,
            (401, _) => PrivateSyncState.NeedsReauthentication,
            (410, "sync_reset") when typeof(T) == typeof(SyncChangesPage) => PrivateSyncState.ResetRequired,
            (410, "manifest_expired") when typeof(T) == typeof(SyncResyncPage) => PrivateSyncState.ManifestExpired,
            (429, _) => PrivateSyncState.RateLimited,
            (503, _) => PrivateSyncState.Unavailable,
            _ => PrivateSyncState.InvalidResponse
        };
        var retry = response.Headers.RetryAfter;
        var delay = retry?.Delta ?? (retry?.Date is { } date ? date - clock.GetUtcNow() : (TimeSpan?)null);
        return new(state, retryAfter: state == PrivateSyncState.RateLimited && delay is { } duration
            ? TimeSpan.FromSeconds(Math.Clamp(duration.TotalSeconds, 0, 300)) : null);
    }
}
