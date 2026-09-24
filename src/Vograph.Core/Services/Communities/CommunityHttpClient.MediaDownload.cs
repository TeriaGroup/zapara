using System.Security.Cryptography;
using Zapara.Contracts.Communities;

namespace Vograph.Core.Services.Communities;

public sealed partial class CommunityHttpClient
{
    public Task<byte[]> ReadMediaAsync(string accessToken, Guid conversationId, Guid messageId, CancellationToken ct = default)
    {
        var path = "/conversations/" + Id(conversationId) + "/messages/" + Id(messageId) + "/media";
        return ReadMediaCoreAsync(Access(accessToken), path, ct);
    }

    private async Task<byte[]> ReadMediaCoreAsync(string access, string path, CancellationToken caller)
    {
        if (legacyRoutes) return await ReadMediaVersionAsync(access, path, 1, caller).ConfigureAwait(false);
        try { return await ReadMediaVersionAsync(access, path, 2, caller).ConfigureAwait(false); }
        catch (CommunityClientException e) when (e.Status == 404 && e.Failure == CommunityClientFailure.ServerUnavailable)
        {
            legacyRoutes = true;
            return await ReadMediaVersionAsync(access, path, 1, caller).ConfigureAwait(false);
        }
    }

    private async Task<byte[]> ReadMediaVersionAsync(string access, string path, int version, CancellationToken caller)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30), clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(caller, timeout.Token);
        var ct = deadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Scope.BaseUri, $"api/v{version}/communities" + path));
        try
        {
            ct.ThrowIfCancellationRequested();
            if (http.DefaultRequestHeaders.Any()) throw new CommunityClientException(CommunityClientFailure.InvalidRequest);
            request.Headers.Authorization = new("Bearer", access);
            request.Headers.Accept.Add(new("application/octet-stream"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode != 200)
            {
                var error = await ReadAsync(response.Content, 4096, ct).ConfigureAwait(false);
                try { throw Error(response, error); }
                finally { CryptographicOperations.ZeroMemory(error); }
            }
            if (response.Content.Headers.ContentType?.MediaType != "application/octet-stream" ||
                response.Content.Headers.ContentEncoding.Count != 0)
                throw new CommunityClientException(CommunityClientFailure.InvalidPayload);
            const int limit = 8 * 1024 * 1024;
            if (response.Content.Headers.ContentLength is > limit or 0)
                throw new CommunityClientException(response.Content.Headers.ContentLength == 0
                    ? CommunityClientFailure.InvalidPayload : CommunityClientFailure.BodyTooLarge);
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            try
            {
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (read == 0) break;
                    if (output.Length + read > limit) throw new CommunityClientException(CommunityClientFailure.BodyTooLarge);
                    output.Write(buffer, 0, read);
                }
                if (output.Length == 0) throw new CommunityClientException(CommunityClientFailure.InvalidPayload);
                return output.ToArray();
            }
            finally { CryptographicOperations.ZeroMemory(buffer); }
        }
        catch (OperationCanceledException)
        {
            if (caller.IsCancellationRequested) throw new OperationCanceledException("Операция отменена.", caller);
            throw new CommunityClientException(CommunityClientFailure.Timeout);
        }
        catch (HttpRequestException) { throw new CommunityClientException(CommunityClientFailure.Transport); }
        catch (IOException) { throw new CommunityClientException(CommunityClientFailure.Transport); }
        finally { request.Headers.Authorization = null; }
    }
}
