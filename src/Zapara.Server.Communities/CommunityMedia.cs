using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Zapara.Server.Communities;

internal static class CommunityMedia
{
    internal const int MaxBytes = 8 * 1024 * 1024;
    internal const int MaxVoiceBytes = 2 * 1024 * 1024;
    private const int MaxVoiceDurationMs = 180_000;
    private const int MaxCircleDurationMs = 60_000;

    internal static async Task<IResult> Post(HttpContext context, string token)
    {
        var (kind, name, bytes, reply, durationMs, topicId) = await Read(context);
        var message = await context.RequestServices.GetRequiredService<CommunityService>().SendMediaAsync(
            token, CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), kind, name, bytes, reply, durationMs, context.RequestAborted, topicId);
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

    private static async Task<(string Kind, string Name, byte[] Bytes, Guid? Reply, int? DurationMs, Guid? TopicId)> Read(HttpContext context)
    {
        CommunityHttpInput.Query(context);
        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var media) ||
            !string.Equals(media.MediaType.Value, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            throw new CommunityInputException(415);
        var kind = One(context, "X-Zapara-Kind");
        if (kind is not ("image" or "video" or "file" or "voice" or "circle")) throw new CommunityInputException();
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
        Guid? topicId = null;
        if (context.Request.Headers.TryGetValue("X-Zapara-Topic", out var topicValues))
        {
            if (topicValues.Count != 1 || !Guid.TryParseExact(topicValues.ToString(), "D", out var id) || id == Guid.Empty)
                throw new CommunityInputException();
            topicId = id;
        }
        int? durationMs = null;
        if (context.Request.Headers.TryGetValue("X-Zapara-Duration-Ms", out var durationValues))
        {
            if (durationValues.Count != 1 ||
                !int.TryParse(durationValues[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                throw new CommunityInputException();
            durationMs = parsed;
        }
        if (!ValidDuration(kind, durationMs)) throw new CommunityInputException();
        var maximum = kind == "voice" ? MaxVoiceBytes : MaxBytes;
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = maximum + 1024;
        if (context.Request.ContentLength > maximum) throw new CommunityInputException(413);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await context.Request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), context.RequestAborted);
            if (count == 0) break;
            if (output.Length + count > maximum) throw new CommunityInputException(413);
            output.Write(buffer, 0, count);
        }
        if (output.Length == 0) throw new CommunityInputException();
        return (kind, name, output.ToArray(), reply, durationMs, topicId);
    }

    internal static void ValidateRecording(string kind, ReadOnlySpan<byte> bytes, int? durationMs)
    {
        if (!ValidDuration(kind, durationMs)) throw CommunityServiceException.InvalidRequest();
        if (kind is not ("voice" or "circle"))
            return;
        if (bytes.Length < 12) throw CommunityServiceException.InvalidRequest();

        var webm = bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3;
        var mp4 = bytes[4] == (byte)'f' && bytes[5] == (byte)'t' && bytes[6] == (byte)'y' && bytes[7] == (byte)'p';
        if (kind == "voice")
        {
            var ogg = bytes[0] == (byte)'O' && bytes[1] == (byte)'g' && bytes[2] == (byte)'g' && bytes[3] == (byte)'S';
            var mp3 = (bytes[0] == (byte)'I' && bytes[1] == (byte)'D' && bytes[2] == (byte)'3') ||
                (bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0);
            if (!webm && !ogg && !mp4 && !mp3) throw CommunityServiceException.InvalidRequest();
        }
        else if (!webm && (!mp4 ||
            bytes[8] == (byte)'M' && bytes[9] == (byte)'4' &&
            (bytes[10] == (byte)'A' || bytes[10] == (byte)'B') && bytes[11] == (byte)' '))
            throw CommunityServiceException.InvalidRequest();
    }

    private static bool ValidDuration(string kind, int? durationMs) => kind switch
    {
        "voice" => durationMs is >= 1 and <= MaxVoiceDurationMs,
        "circle" => durationMs is >= 1 and <= MaxCircleDurationMs,
        _ => durationMs is null
    };

    private static string One(HttpContext context, string name)
    {
        if (!context.Request.Headers.TryGetValue(name, out var values) || values.Count != 1) throw new CommunityInputException();
        var text = values.ToString();
        if (text.Length is < 1 or > 600) throw new CommunityInputException();
        return text;
    }
}
