using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Zapara.Server.Communities;

internal static class CommunityMedia
{
    internal const int MaxBytes = 8 * 1024 * 1024;

    internal static async Task<IResult> Post(HttpContext context, string token)
    {
        var (kind, name, bytes, reply) = await Read(context);
        var message = await context.RequestServices.GetRequiredService<CommunityService>().SendMediaAsync(
            token, CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), kind, name, bytes, reply, context.RequestAborted);
        return CommunityHttpResult.Json(message, 201);
    }

    internal static async Task<IResult> Get(HttpContext context, string token)
    {
        CommunityHttpInput.Query(context);
        var bytes = await context.RequestServices.GetRequiredService<CommunityService>().ReadMediaAsync(
            token,
            CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]),
            CommunityHttpInput.Id(context.Request.RouteValues["messageId"]),
            context.RequestAborted);
        return CommunityHttpResult.Bytes(bytes);
    }

    private static async Task<(string Kind, string Name, byte[] Bytes, Guid? Reply)> Read(HttpContext context)
    {
        CommunityHttpInput.Query(context);
        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var media) ||
            !string.Equals(media.MediaType.Value, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            throw new CommunityInputException(415);
        var kind = One(context, "X-Zapara-Kind");
        if (kind is not ("image" or "video" or "file")) throw new CommunityInputException();
        var encoded = One(context, "X-Zapara-Name");
        string name;
        try { name = Uri.UnescapeDataString(encoded); }
        catch (UriFormatException) { throw new CommunityInputException(); }
        Guid? reply = null;
        if (context.Request.Headers.TryGetValue("X-Zapara-Reply", out var replyValues) && replyValues.Count > 0)
        {
            if (replyValues.Count != 1 || !Guid.TryParseExact(replyValues.ToString(), "D", out var id) || id == Guid.Empty)
                throw new CommunityInputException();
            reply = id;
        }
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = MaxBytes + 1024;
        if (context.Request.ContentLength > MaxBytes) throw new CommunityInputException(413);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await context.Request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), context.RequestAborted);
            if (count == 0) break;
            if (output.Length + count > MaxBytes) throw new CommunityInputException(413);
            output.Write(buffer, 0, count);
        }
        if (output.Length == 0) throw new CommunityInputException();
        return (kind, name, output.ToArray(), reply);
    }

    private static string One(HttpContext context, string name)
    {
        if (!context.Request.Headers.TryGetValue(name, out var values) || values.Count != 1) throw new CommunityInputException();
        var text = values.ToString();
        if (text.Length is < 1 or > 600) throw new CommunityInputException();
        return text;
    }
}
