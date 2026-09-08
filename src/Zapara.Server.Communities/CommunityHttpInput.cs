using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Zapara.Contracts.Communities;

namespace Zapara.Server.Communities;

internal static class CommunityHttpInput
{
    internal static void Query(HttpContext context, params string[] allowed)
    {
        if (context.Request.Query.Any(pair => !allowed.Contains(pair.Key, StringComparer.Ordinal) || pair.Value.Count != 1))
            throw new CommunityInputException();
    }
    internal static Guid Id(object? raw)
    {
        var text = raw as string;
        return Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty && text == id.ToString("D")
            ? id : throw new CommunityInputException();
    }
    internal static string? GroupId(HttpContext context)
    {
        if (!context.Request.Query.TryGetValue("groupId", out var raw)) return null;
        try { return CommunityValidation.GroupId(raw[0]); }
        catch (ArgumentException) { throw new CommunityInputException(); }
    }
    internal static async Task<T> Body<T>(HttpContext context)
    {
        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var media) ||
            !string.Equals(media.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase) ||
            (media.Charset.HasValue && !string.Equals(media.Charset.Value.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)))
            throw new CommunityInputException(415);
        var bytes = await Bytes(context);
        try { return CommunityJson.Parse<T>(bytes); }
        catch (ArgumentException) { throw new CommunityInputException(); }
    }
    internal static async Task Empty(HttpContext context)
    {
        if ((await Bytes(context)).Any(b => b is not (0x20 or 0x09 or 0x0a or 0x0d))) throw new CommunityInputException();
    }
    private static async Task<byte[]> Bytes(HttpContext context)
    {
        const int maximum = CommunityValidation.RequestBytes;
        if (context.Request.ContentLength > maximum) throw new CommunityInputException(413);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await context.Request.Body.ReadAsync(buffer.AsMemory(0,
                Math.Min(buffer.Length, maximum + 1 - (int)output.Length)), context.RequestAborted);
            if (count == 0) return output.ToArray();
            output.Write(buffer, 0, count);
            if (output.Length > maximum) throw new CommunityInputException(413);
        }
    }
}

internal sealed class CommunityInputException(int status = 400) : Exception("Некорректный запрос сообщества")
{
    internal int Status { get; } = status;
}
