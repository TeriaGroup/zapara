using System.Text;
using System.Text.Json;

namespace Zapara.Server.Accounts.ExternalProviders;

internal sealed class ProviderTransport(HttpClient http)
{
    private const int MaxBytes = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try { return await ReadResponseAsync(request, ct).ConfigureAwait(false); }
        finally
        {
            // HttpRequestMessage.Dispose alone retains its content and Authorization references.
            request.Content?.Dispose();
            request.Content = null;
            request.Headers.Authorization = null;
        }
    }

    private async Task<JsonDocument> ReadResponseAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new ExternalProviderException(ExternalProviderFailure.ProviderRejected);
        if (response.Content.Headers.ContentEncoding.Count != 0 || response.Content.Headers.ContentLength > MaxBytes)
            throw new ExternalProviderException(ExternalProviderFailure.InvalidResponse);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        // Count actual bytes (including for chunked/misreported responses), not just Content-Length.
        var bytes = new byte[MaxBytes + 1];
        var count = 0;
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count, bytes.Length - count), ct).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
                if (count > MaxBytes) throw new ExternalProviderException(ExternalProviderFailure.InvalidResponse);
            }
            ct.ThrowIfCancellationRequested();
            return Parse(bytes, count);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }

    private static JsonDocument Parse(byte[] bytes, int count)
    {
        // Decode synchronously so the full token payload is not retained in an async state machine.
        var text = StrictUtf8.GetString(bytes, 0, count);
        var document = JsonDocument.Parse(text, new() { MaxDepth = 16 });
        try
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ExternalProviderException(ExternalProviderFailure.InvalidResponse);
            RejectDuplicates(document.RootElement);
            if (document.RootElement.TryGetProperty("error", out _))
                throw new ExternalProviderException(ExternalProviderFailure.ProviderRejected);
            return document;
        }
        catch { document.Dispose(); throw; }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ExternalProviderException(ExternalProviderFailure.InvalidResponse);
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
}
