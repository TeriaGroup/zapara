using System.Text;
using System.Text.Json;
using Microsoft.Net.Http.Headers;
using Zapara.Contracts.Accounts;

namespace Zapara.Server.Accounts;

internal static class AccountBodyReader
{
    internal const int MaximumBytes = 16 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = AccountJson.CreateOptions();

    internal static async Task<T> Read<T>(HttpContext context)
    {
        using var document = await Document(context);
        try { return document.RootElement.Deserialize<T>(Json) ?? throw new AccountBodyException(); }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        { throw new AccountBodyException(); }
    }

    internal static async Task<string> Refresh(HttpContext context)
    {
        using var document = await Document(context);
        var root = document.RootElement;
        if (root.EnumerateObject().Count() != 1 || !root.TryGetProperty("refreshToken", out var token) ||
            token.ValueKind != JsonValueKind.String) throw new AccountBodyException();
        // The service, not DTO construction, maps invalid credential strings to invalid_session.
        return token.GetString()!;
    }

    internal static async Task Empty(HttpContext context)
    {
        var bytes = await Bytes(context);
        // Empty-body actions accept only empty bytes / JSON whitespace, not even an empty object.
        if (bytes.Any(b => b is not (0x20 or 0x09 or 0x0a or 0x0d))) throw new AccountBodyException();
    }

    private static async Task<JsonDocument> Document(HttpContext context)
    {
        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var media) ||
            !string.Equals(media.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase) ||
            (media.Charset.HasValue && !string.Equals(media.Charset.Value.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)))
            throw new AccountBodyException(415);
        var bytes = await Bytes(context);
        JsonDocument? document = null;
        try
        {
            StrictUtf8.GetCharCount(bytes);
            document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new AccountBodyException();
            UniqueMembers(document.RootElement);
            return document;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or AccountBodyException or InvalidOperationException)
        {
            document?.Dispose();
            throw new AccountBodyException();
        }
    }

    private static void UniqueMembers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new AccountBodyException();
                UniqueMembers(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) UniqueMembers(child);
    }

    private static async Task<byte[]> Bytes(HttpContext context)
    {
        if (context.Request.ContentLength > MaximumBytes) throw new AccountBodyException(413);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await context.Request.Body.ReadAsync(buffer.AsMemory(0,
                Math.Min(buffer.Length, MaximumBytes + 1 - (int)output.Length)), context.RequestAborted);
            if (count == 0) return output.ToArray();
            output.Write(buffer, 0, count);
            if (output.Length > MaximumBytes) throw new AccountBodyException(413);
        }
    }
}
