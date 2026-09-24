using System.Text.Json;
using Zapara.Server.Accounts;
using Zapara.Server.Communities;
using Zapara.Server.Social;
using Zapara.Server.Web;

namespace Zapara.Server.Operator;

public static class SupportEndpoints
{
    public static WebApplication MapSupport(this WebApplication app)
    {
        if (!AccountsConfiguration.IsEnabled(app.Configuration)) return app;
        app.MapGet("/api/v1/support", (Delegate)ListNative);
        app.MapPost("/api/v1/support", (Delegate)OpenNative);
        app.MapPost("/api/v1/support/{id:guid}", (Delegate)ContinueNative);
        app.MapGet("/api/v1/support/attachments/{id:guid}", (Delegate)AttachmentNative);
        app.MapPost("/web-api/support", (Delegate)OpenWeb);
        app.MapGet("/web-api/support", (Delegate)ListWeb);
        app.MapPost("/web-api/support/{id:guid}", (Delegate)ContinueWeb);
        app.MapGet("/web-api/support/attachments/{id:guid}", (Delegate)AttachmentWeb);
        app.MapPost("/api/v1/files", (Delegate)FileNative);
        app.MapGet("/api/v1/files/{name}", (Delegate)ReadFile);
        app.MapPost("/web-api/files", (Delegate)FileWeb);
        app.MapGet("/web-api/files/{name}", (Delegate)ReadFile);
        app.MapPost("/api/v1/homework-files", (Delegate)HomeworkFile);
        app.MapGet("/api/v1/homework-files/{name}", (Delegate)ReadFile);
        app.MapPost("/web-api/homework-files", (Delegate)HomeworkFile);
        app.MapGet("/web-api/homework-files/{name}", (Delegate)ReadFile);
        return app;
    }

    private static async Task<IResult> OpenNative(HttpContext context)
    {
        var user = await Me(context, Bearer(context));
        return await Open(context, user.User.UserId);
    }

    private static async Task<IResult> ListNative(HttpContext context)
    {
        var user = await Me(context, Bearer(context));
        var list = await context.RequestServices.GetRequiredService<SupportStore>().ListAsync(user.User.UserId, context.RequestAborted);
        return Results.Json(list.Select(ticket => JsonSerializer.Deserialize<JsonElement>(SupportStore.Write(ticket))));
    }

    private static async Task<IResult> OpenWeb(HttpContext context)
    {
        var token = await WebToken(context);
        var user = await Me(context, token);
        return await Open(context, user.User.UserId);
    }

    private static async Task<IResult> ListWeb(HttpContext context)
    {
        var token = await WebToken(context);
        var user = await Me(context, token);
        var list = await context.RequestServices.GetRequiredService<SupportStore>().ListAsync(user.User.UserId, context.RequestAborted);
        return Results.Json(list.Select(ticket => JsonSerializer.Deserialize<JsonElement>(SupportStore.Write(ticket))));
    }

    private static async Task<IResult> ContinueNative(HttpContext context, Guid id)
    {
        var user = await Me(context, Bearer(context));
        return await Continue(context, user.User.UserId, id);
    }

    private static async Task<IResult> ContinueWeb(HttpContext context, Guid id)
    {
        var user = await Me(context, await WebToken(context));
        return await Continue(context, user.User.UserId, id);
    }

    private static async Task<IResult> Continue(HttpContext context, Guid userId, Guid id)
    {
        var report = await ReadReport(context);
        var ticket = await context.RequestServices.GetRequiredService<SupportStore>().ContinueAsync(userId, id, report.Body, context.RequestAborted, report.Files, Token(context));
        return Results.Text(SupportStore.Write(ticket), "application/json");
    }

    private static async Task<IResult> Open(HttpContext context, Guid userId)
    {
        var report = await ReadReport(context);
        var ticket = await context.RequestServices.GetRequiredService<SupportStore>().OpenAsync(userId, report.Subject, report.Body, context.RequestAborted, report.Files, Token(context));
        return Results.Text(SupportStore.Write(ticket), "application/json");
    }

    private static async Task<IResult> AttachmentNative(HttpContext context, Guid id)
    {
        var user = await Me(context, Bearer(context));
        return await Attachment(context, user.User.UserId, id);
    }

    private static async Task<IResult> AttachmentWeb(HttpContext context, Guid id)
    {
        var user = await Me(context, await WebToken(context, bootstrap: true));
        return await Attachment(context, user.User.UserId, id);
    }

    private static async Task<IResult> Attachment(HttpContext context, Guid userId, Guid id)
    {
        var found = await context.RequestServices.GetRequiredService<SupportStore>().ReadFileAsync(userId, id, context.RequestAborted);
        if (found is null) return Results.NotFound();
        var file = found.Value;
        var name = file.Name.Replace("\"", "").Replace("\r", "").Replace("\n", "");
        var disposition = file.Kind == "photo" ? "inline" : "attachment";
        context.Response.Headers.ContentDisposition = $"{disposition}; filename*=UTF-8''{Uri.EscapeDataString(name)}";
        context.Response.Headers.CacheControl = "private, no-store";
        return Results.Bytes(file.Bytes, file.Type);
    }

    private static async Task<(string Subject, string Body, IReadOnlyList<SupportFile> Files)> ReadReport(HttpContext context)
    {
        if (!context.Request.HasFormContentType)
        {
            var body = await context.Request.ReadFromJsonAsync<SupportBody>(context.RequestAborted) ?? throw new AccountBodyException();
            return (body.Subject ?? "", body.Body ?? "", []);
        }
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        if (form.Files.GetFiles("photo").Count > SupportFiles.MaxEach)
            throw new SupportAttachmentException("Можно приложить не больше трёх фотографий.");
        if (form.Files.GetFiles("log").Count > SupportFiles.MaxEach)
            throw new SupportAttachmentException("Можно приложить не больше трёх логов.");
        var files = new List<SupportFile>();
        await Take(form, "photo", SupportFiles.PhotoBytes, "Фото больше 4 МиБ.", files, context.RequestAborted);
        await Take(form, "log", SupportFiles.LogBytes, "Лог больше 512 КиБ.", files, context.RequestAborted);
        return (form["subject"].ToString(), form["body"].ToString(), files);
    }

    private static async Task Take(IFormCollection form, string kind, int max, string tooBig, List<SupportFile> files, CancellationToken ct)
    {
        foreach (var file in form.Files.GetFiles(kind))
        {
            if (file.Length <= 0) throw new SupportAttachmentException("Файл пустой.");
            if (file.Length > max) throw new SupportAttachmentException(tooBig);
            var bytes = new byte[file.Length];
            await using var stream = file.OpenReadStream();
            await stream.ReadExactlyAsync(bytes, ct);
            files.Add(SupportFiles.Inspect(kind, file.FileName, bytes));
        }
    }

    private static string Token(HttpContext context) => context.Items["support-token"] as string ?? Bearer(context);

    private static async Task<IResult> FileNative(HttpContext context)
    {
        var user = await Me(context, Bearer(context));
        return await SaveFile(context, user.User.UserId);
    }

    private static async Task<IResult> FileWeb(HttpContext context)
    {
        var user = await Me(context, await WebToken(context));
        return await SaveFile(context, user.User.UserId);
    }

    private static async Task<IResult> HomeworkFile(HttpContext context)
    {
        var token = context.Request.Path.StartsWithSegments("/web-api") ? await WebToken(context) : Bearer(context);
        var user = await Me(context, token);
        if (!context.Request.HasFormContentType) return Results.BadRequest();
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var file = form.Files.Count == 1 ? form.Files[0] : null;
        if (file is null || file.Length is <= 0 or > 20_000_000) return Results.BadRequest();
        if (!Guid.TryParse(form["communityId"], out var communityId) || !Guid.TryParse(form["homeworkId"], out var homeworkId))
            return Results.BadRequest();
        await context.RequestServices.GetRequiredService<CommunityService>().GetHomeworkAsync(token, communityId, homeworkId, context.RequestAborted);
        var bytes = new byte[file.Length];
        await using (var stream = file.OpenReadStream())
            await stream.ReadExactlyAsync(bytes, context.RequestAborted);
        var name = ContentNames.HomeworkFile(Guid.NewGuid());
        var stored = await context.RequestServices.GetRequiredService<StudentUpload>().Accept(
            context.RequestServices.GetRequiredService<IAccountUnitOfWork>(), token, form["groupId"].ToString(), name, bytes, context.RequestAborted);
        _ = user;
        return Results.Json(new { name, bytes = stored.LongLength });
    }

    private static async Task<IResult> SaveFile(HttpContext context, Guid userId)
    {
        if (!context.Request.HasFormContentType) return Results.BadRequest();
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var file = form.Files.Count == 1 ? form.Files[0] : null;
        if (file is null || file.Length is <= 0 or > 20_000_000) return Results.BadRequest();
        var bytes = new byte[file.Length];
        await using var stream = file.OpenReadStream();
        await stream.ReadExactlyAsync(bytes, context.RequestAborted);
        var group = form["groupId"].ToString();
        var token = context.Items["support-token"] as string ?? Bearer(context);
        var name = Guid.NewGuid().ToString("N") + ".bin";
        _ = userId;
        var stored = await context.RequestServices.GetRequiredService<StudentUpload>().Accept(
            context.RequestServices.GetRequiredService<IAccountUnitOfWork>(), token, group, name, bytes, context.RequestAborted);
        return Results.Json(new { name, bytes = stored.LongLength });
    }

    private static IResult ReadFile(HttpContext context, string name)
    {
        var bytes = context.RequestServices.GetRequiredService<IObjectStore>().Get(name);
        return bytes is null ? Results.NotFound() : Results.Bytes(bytes, "application/octet-stream");
    }

    private static async Task<Contracts.Accounts.MeResponse> Me(HttpContext context, string token)
    {
        context.Items["support-token"] = token;
        return await context.RequestServices.GetRequiredService<AccountService>().GetMeAsync(token, context.RequestAborted);
    }

    private static string Bearer(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || header.Length < 12) throw new AccountServiceException(AccountFailure.InvalidSession);
        return header["Bearer ".Length..].Trim();
    }

    private static async Task<string> WebToken(HttpContext context, bool bootstrap = false)
    {
        var store = context.RequestServices.GetService<WebSessionStore>() ?? throw new AccountServiceException(AccountFailure.InvalidSession);
        return await store.UseAsync(context, token => Task.FromResult(token), bootstrap);
    }

    private sealed record SupportBody(string? Subject, string? Body);
}
