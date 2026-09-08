using System.Globalization;
using Microsoft.Net.Http.Headers;
using Zapara.Contracts.Sync;

namespace Zapara.Server.Sync;

internal static class SyncHttpInput
{
    internal static void Query(HttpContext context, params string[] allowed)
    {
        if (context.Request.Query.Any(pair => !allowed.Contains(pair.Key, StringComparer.Ordinal) || pair.Value.Count != 1))
            throw new SyncInputException();
    }
    internal static Guid Id(string? raw)
        => Guid.TryParseExact(raw, "D", out var id) && id != Guid.Empty && raw == id.ToString("D")
            ? id : throw new SyncInputException();
    internal static long After(HttpContext context, string key)
    {
        if (!context.Request.Query.TryGetValue(key, out var raw)) return 0;
        return long.TryParse(raw[0], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value : throw new SyncInputException();
    }
    internal static int Limit(HttpContext context)
    {
        if (!context.Request.Query.TryGetValue("limit", out var raw)) return 100;
        return int.TryParse(raw[0], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 1 and <= 200
            ? value : throw new SyncInputException();
    }
    internal static async Task<SyncMutation> Mutation(HttpContext context)
    {
        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var media) ||
            !string.Equals(media.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase) ||
            (media.Charset.HasValue && !string.Equals(media.Charset.Value.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)))
            throw new SyncInputException();
        var bytes = await Bytes(context);
        try { return SyncJson.Parse<SyncMutation>(bytes); }
        catch (ArgumentException) { throw new SyncInputException(); }
    }
    internal static async Task Empty(HttpContext context)
    {
        // Same empty-action policy as W1: JSON whitespace is allowed, even {} is not.
        if ((await Bytes(context)).Any(b => b is not (0x20 or 0x09 or 0x0a or 0x0d))) throw new SyncInputException();
    }
    private static async Task<byte[]> Bytes(HttpContext context)
    {
        const int maximum = SyncValidation.RequestBytes;
        if (context.Request.ContentLength > maximum) throw new SyncInputException(413);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await context.Request.Body.ReadAsync(buffer.AsMemory(0,
                Math.Min(buffer.Length, maximum + 1 - (int)output.Length)), context.RequestAborted);
            if (count == 0) return output.ToArray();
            output.Write(buffer, 0, count);
            if (output.Length > maximum) throw new SyncInputException(413);
        }
    }
}

internal sealed class SyncInputException(int status = 400) : Exception("Некорректный запрос Sync")
{
    internal int Status { get; } = status;
}
