using System.Globalization;
using Microsoft.AspNetCore.Http.Features;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Social;
using Zapara.Server.Accounts;
using Zapara.Server.Social;

namespace Zapara.Server.Web;

internal static class SocialHttp
{
    internal static void MapNative(WebApplication app)
    {
        if (!AccountsConfiguration.IsEnabled(app.Configuration)) return;
        MapVersion(app, 1);
        MapVersion(app, 2);
    }

    internal static IResult Problem(SocialException exception) => Results.Json(
        new AccountError(exception.Status switch
        {
            400 or 413 or 415 => "Некорректный запрос",
            404 => "Не найдено",
            503 => "Сервис временно недоступен",
            _ => "Внутренняя ошибка сервера"
        }, exception.Status, exception.Code),
        AccountJson.CreateOptions(), "application/problem+json", exception.Status);

    internal static async Task<IResult> Home(HttpContext context)
    {
        NoQuery(context);
        return Json(await Service(context).HomeAsync(Access(context), context.RequestAborted));
    }

    internal static async Task<IResult> Invite(HttpContext context)
    {
        NoQuery(context);
        var body = await AccountBodyReader.Read<SocialInviteRequest>(context);
        return Json(await Service(context).InviteAsync(Access(context), body.Code, context.RequestAborted));
    }

    internal static async Task<IResult> Accept(HttpContext context)
    {
        NoQuery(context);
        await AccountBodyReader.Empty(context);
        return Json(await Service(context).AcceptAsync(Access(context), RouteId(context, "friendshipId"), context.RequestAborted));
    }

    internal static async Task<IResult> Decline(HttpContext context)
    {
        NoQuery(context);
        await AccountBodyReader.Empty(context);
        return Json(await Service(context).DeclineAsync(Access(context), RouteId(context, "friendshipId"), context.RequestAborted));
    }

    internal static async Task<IResult> Messages(HttpContext context)
    {
        var before = Before(context);
        return Json(await Service(context).MessagesAsync(Access(context), RouteId(context, "conversationId"), before, context.RequestAborted));
    }

    internal static async Task<IResult> Text(HttpContext context)
    {
        NoQuery(context);
        var body = await AccountBodyReader.Read<SocialTextRequest>(context);
        return Json(await Service(context).SendTextAsync(Access(context), RouteId(context, "conversationId"), body.Body, body.ReplyTo, context.RequestAborted), 201);
    }

    internal static async Task<IResult> Edit(HttpContext context)
    {
        NoQuery(context);
        var body = await AccountBodyReader.Read<SocialTextRequest>(context);
        return Json(await Service(context).EditAsync(Access(context), RouteId(context, "conversationId"), RouteId(context, "messageId"), body.Body, context.RequestAborted));
    }

    internal static async Task<IResult> Delete(HttpContext context)
    {
        NoQuery(context);
        await AccountBodyReader.Empty(context);
        return Json(await Service(context).DeleteAsync(Access(context), RouteId(context, "conversationId"), RouteId(context, "messageId"), context.RequestAborted));
    }

    internal static async Task<IResult> React(HttpContext context)
    {
        NoQuery(context);
        var body = await AccountBodyReader.Read<SocialReactionRequest>(context);
        return Json(await Service(context).ReactAsync(Access(context), RouteId(context, "conversationId"), RouteId(context, "messageId"), body.Emoji, context.RequestAborted));
    }

    internal static async Task<IResult> Card(HttpContext context)
    {
        NoQuery(context);
        var body = await AccountBodyReader.Read<SocialCardRequest>(context);
        return Json(await Service(context).SendCardAsync(Access(context), RouteId(context, "conversationId"), body.Body, body.ReplyTo, context.RequestAborted), 201);
    }

    internal static async Task<IResult> Sticker(HttpContext context)
    {
        NoQuery(context);
        var body = await AccountBodyReader.Read<SocialStickerRequest>(context);
        return Json(await Service(context).SendStickerAsync(Access(context), RouteId(context, "conversationId"), body.Sticker, body.ReplyTo, context.RequestAborted), 201);
    }

    internal static Task<IResult> Image(HttpContext context) => Upload(context, UploadKind.Image);

    internal static Task<IResult> Document(HttpContext context) => Upload(context, UploadKind.File);

    internal static Task<IResult> Voice(HttpContext context) => Upload(context, UploadKind.Voice);

    internal static Task<IResult> Circle(HttpContext context) => Upload(context, UploadKind.Circle);

    internal static async Task<IResult> Attachment(HttpContext context)
    {
        NoQuery(context);
        var opened = await Service(context).OpenAsync(Access(context), RouteId(context, "attachmentId"), context.RequestAborted);
        return new AttachmentResult(opened.Stream, opened.Name, opened.Type, opened.Image);
    }

    private static void MapVersion(WebApplication app, int version)
    {
        var group = app.MapGroup($"/api/v{version}/social").RequireAuthorization("AccountUser").RequireRateLimiting("account-other");
        Route(group, "GET", "/home", Home);
        Route(group, "POST", "/invites", Invite);
        Route(group, "POST", "/invites/{friendshipId}/accept", Accept);
        Route(group, "POST", "/invites/{friendshipId}/decline", Decline);
        Route(group, "GET", "/conversations/{conversationId}/messages", Messages);
        Route(group, "POST", "/conversations/{conversationId}/messages", Text);
        Route(group, "POST", "/conversations/{conversationId}/stickers", Sticker);
        Route(group, "POST", "/conversations/{conversationId}/cards", Card);
        Route(group, "POST", "/conversations/{conversationId}/messages/{messageId}/edit", Edit);
        Route(group, "POST", "/conversations/{conversationId}/messages/{messageId}/delete", Delete);
        Route(group, "POST", "/conversations/{conversationId}/messages/{messageId}/reaction", React);
        Route(group, "POST", "/conversations/{conversationId}/images", Image);
        Route(group, "POST", "/conversations/{conversationId}/files", Document);
        Route(group, "POST", "/conversations/{conversationId}/voice", Voice);
        Route(group, "POST", "/conversations/{conversationId}/circles", Circle);
        Route(group, "GET", "/attachments/{attachmentId}", Attachment);
    }

    private static void Route(RouteGroupBuilder group, string method, string path, Func<HttpContext, Task<IResult>> handler)
        => group.MapMethods(path, [method], (Delegate)(Func<HttpContext, Task<IResult>>)(async context =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            try { return await handler(context); }
            catch (SocialException exception) { return Problem(exception); }
            catch (AccountBodyException exception) { return Problem(new SocialException(exception.Status, exception.Status == 413 ? "payload_too_large" : "invalid_request")); }
            catch (AccountServiceException exception)
            {
                if (exception.Failure == AccountFailure.RateLimited) context.Response.Headers.RetryAfter = "60";
                return AccountErrors.From(exception);
            }
            catch (BadHttpRequestException exception) when (exception.StatusCode == 413) { return Problem(new SocialException(413, "payload_too_large")); }
        }));

    private static async Task<IResult> Upload(HttpContext context, UploadKind kind)
    {
        NoQuery(context);
        var conversationId = RouteId(context, "conversationId");
        var cap = kind switch
        {
            UploadKind.Image => (long)PhotoCompressor.MaxInputBytes,
            UploadKind.Voice => VoicePolicy.MaxBytes,
            UploadKind.Circle => CirclePolicy.MaxBytes,
            _ => DocumentPolicy.MaxBytes
        };
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = cap + 1024 * 1024;
        if (!context.Request.HasFormContentType) throw new SocialException(415, "invalid_request");
        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(new FormOptions
            {
                MultipartBodyLengthLimit = cap + 1024 * 1024,
                ValueLengthLimit = 1024,
                ValueCountLimit = 8,
                KeyLengthLimit = 80,
                MultipartHeadersCountLimit = 16
            }, context.RequestAborted);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == 413) { throw new SocialException(413, "payload_too_large"); }
        catch (InvalidDataException) { throw new SocialException(400, "invalid_request"); }
        if (form.Files.Count != 1) throw new SocialException(400, "invalid_request");
        var file = form.Files[0];
        if (file.Length <= 0) throw new SocialException(400, "invalid_request");
        if (file.Length > cap) throw new SocialException(413, "payload_too_large");
        var bytes = new byte[(int)file.Length];
        await using (var stream = file.OpenReadStream())
        {
            var read = 0;
            while (read < bytes.Length)
            {
                var count = await stream.ReadAsync(bytes.AsMemory(read), context.RequestAborted);
                if (count == 0) break;
                read += count;
            }
            if (read != bytes.Length) throw new SocialException(400, "invalid_request");
        }
        var reply = FormReply(form);
        var message = kind switch
        {
            UploadKind.Image => await Service(context).SendImageAsync(Access(context), conversationId, bytes, reply, context.RequestAborted),
            UploadKind.Voice => await Service(context).SendVoiceAsync(Access(context), conversationId, bytes, FormDuration(form), reply, context.RequestAborted),
            UploadKind.Circle => await Service(context).SendCircleAsync(Access(context), conversationId, bytes, FormDuration(form), reply, context.RequestAborted),
            _ => await Service(context).SendDocumentAsync(Access(context), conversationId, file.FileName, bytes, reply, context.RequestAborted)
        };
        return Json(message, 201);
    }

    private enum UploadKind { Image, File, Voice, Circle }

    private static Guid? FormReply(IFormCollection form)
    {
        if (!form.TryGetValue("replyTo", out var values) || values.Count == 0 || string.IsNullOrWhiteSpace(values[0])) return null;
        if (values.Count != 1 || !Guid.TryParseExact(values[0], "D", out var id) || id == Guid.Empty) throw new SocialException(400, "invalid_request");
        return id;
    }

    private static int? FormDuration(IFormCollection form)
    {
        if (!form.TryGetValue("durationMs", out var values) || values.Count == 0 || string.IsNullOrWhiteSpace(values[0])) return null;
        if (values.Count != 1 || !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ms))
            throw new SocialException(400, "invalid_request");
        return ms;
    }

    private static SocialService Service(HttpContext context) => context.RequestServices.GetRequiredService<SocialService>();

    private static string Access(HttpContext context)
    {
        if (context.Items[typeof(WebSessionStore)] is string token && token.Length > 0) return token;
        return OpaqueAccountHandler.Bearer(context.Request);
    }

    private static void NoQuery(HttpContext context)
    {
        if (context.Request.Query.Count > 0) throw new SocialException(400, "invalid_request");
    }

    private static Guid? Before(HttpContext context)
    {
        var query = context.Request.Query;
        if (query.Count == 0) return null;
        if (query.Count != 1 || !query.TryGetValue("before", out var values) || values.Count != 1)
            throw new SocialException(400, "invalid_request");
        if (!Guid.TryParseExact(values[0], "D", out var id) || id == Guid.Empty) throw new SocialException(400, "invalid_request");
        return id;
    }

    private static Guid RouteId(HttpContext context, string name)
    {
        var raw = context.Request.RouteValues[name] as string;
        if (!Guid.TryParseExact(raw, "D", out var id) || id == Guid.Empty || raw != id.ToString("D"))
            throw new SocialException(400, "invalid_request");
        return id;
    }

    private static IResult Json<T>(T value, int status = 200)
        => Results.Json(value, AccountJson.CreateOptions(), statusCode: status);

    private sealed class AttachmentResult(Stream stream, string name, string type, bool image) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = type;
            context.Response.Headers.CacheControl = "private, max-age=86400";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers.ContentDisposition = DocumentPolicy.Header(name, image);
            if (stream.CanSeek) context.Response.ContentLength = stream.Length;
            await using (stream)
                await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
    }
}
