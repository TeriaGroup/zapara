using Microsoft.AspNetCore.Http.Features;
using Zapara.Server.Accounts;
using Zapara.Server.Social;

namespace Zapara.Server.Web;

internal static partial class SocialHttp
{
    internal static async Task<IResult> UserAvatar(HttpContext context)
    {
        NoQuery(context);
        var avatar = await Avatars(context).OpenUserAsync(Access(context), RouteId(context, "userId"), context.RequestAborted);
        return new AvatarResult(avatar);
    }

    internal static async Task<IResult> GroupAvatar(HttpContext context)
    {
        NoQuery(context);
        var avatar = await Avatars(context).OpenGroupAsync(Access(context), RouteId(context, "communityId"), context.RequestAborted);
        return new AvatarResult(avatar);
    }

    internal static Task<IResult> PutUserAvatar(HttpContext context) => PutAvatar(context, null);
    internal static Task<IResult> PutGroupAvatar(HttpContext context) => PutAvatar(context, RouteId(context, "communityId"));
    internal static Task<IResult> DeleteUserAvatar(HttpContext context) => DeleteAvatar(context, null);
    internal static Task<IResult> DeleteGroupAvatar(HttpContext context) => DeleteAvatar(context, RouteId(context, "communityId"));

    private static IAvatarService Avatars(HttpContext context) => context.RequestServices.GetRequiredService<IAvatarService>();

    private static async Task<IResult> DeleteAvatar(HttpContext context, Guid? groupId)
    {
        NoQuery(context);
        await AccountBodyReader.Empty(context);
        await Avatars(context).DeleteAsync(Access(context), groupId, context.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> PutAvatar(HttpContext context, Guid? groupId)
    {
        NoQuery(context);
        const int cap = AvatarCompressor.MaxInputBytes;
        const int envelopeCap = cap + 64 * 1024;
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = envelopeCap;
        if (context.Request.ContentLength > envelopeCap) throw new SocialException(413, "payload_too_large");
        if (!context.Request.HasFormContentType) throw new SocialException(415, "invalid_request");
        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(new FormOptions
            {
                MultipartBodyLengthLimit = envelopeCap,
                MemoryBufferThreshold = envelopeCap,
                ValueLengthLimit = 128,
                ValueCountLimit = 1,
                KeyLengthLimit = 80,
                MultipartHeadersLengthLimit = 4096,
                MultipartHeadersCountLimit = 8
            }, context.RequestAborted);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == 413) { throw new SocialException(413, "payload_too_large"); }
        catch (InvalidDataException) { throw new SocialException(400, "invalid_request"); }
        if (form.Count != 0 || form.Files.Count != 1 || form.Files[0].Name != "file") throw new SocialException(400, "invalid_request");
        var file = form.Files[0];
        if (file.Length > cap) throw new SocialException(413, "payload_too_large");
        if (file.Length < 12) throw new SocialException(400, "invalid_image");
        var bytes = new byte[(int)file.Length];
        await using (var stream = file.OpenReadStream())
        {
            try { await stream.ReadExactlyAsync(bytes, context.RequestAborted); }
            catch (EndOfStreamException) { throw new SocialException(400, "invalid_image"); }
        }
        return Json(await Avatars(context).PutAsync(Access(context), groupId, bytes, context.RequestAborted));
    }

    private sealed class AvatarResult(AvatarDownload avatar) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            await using var stream = avatar.Stream;
            var etag = "\"" + avatar.Revision + "\"";
            context.Response.Headers.CacheControl = "private, no-cache";
            context.Response.Headers.ETag = etag;
            context.Response.Headers.Vary = "Authorization, Cookie";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            // Authorization above always runs, even when the client has this revision cached.
            if (context.Request.Headers.IfNoneMatch.ToString().Split(',').Any(value =>
                value.Trim() is "*" || value.Trim() == etag || value.Trim() == "W/" + etag))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }
            context.Response.ContentType = "image/webp";
            context.Response.Headers.ContentDisposition = "inline; filename=\"avatar.webp\"";
            if (stream.CanSeek) context.Response.ContentLength = stream.Length;
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
    }
}
