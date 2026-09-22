using Microsoft.AspNetCore.Http;
using Zapara.Contracts.Communities;
using Zapara.Server.Accounts;

namespace Zapara.Server.Communities;

internal static class CommunityHttpResult
{
    internal static IResult Json<T>(T value, int status = 200) => new TypedJson<T>(value, status);
    internal static IResult Problem(int status, string code)
    {
        var title = status switch
        {
            400 or 413 or 415 => "Некорректный запрос",
            401 => "Не удалось подтвердить доступ",
            403 => "Доступ запрещён",
            404 => "Ресурс не найден",
            409 => "Конфликт изменения",
            429 => "Слишком много запросов",
            503 => "Сервис временно недоступен",
            _ => "Внутренняя ошибка сервера"
        };
        return new FrozenJson(CommunityJson.Serialize(new CommunityError(title, status, code)), status, "application/problem+json");
    }
    internal static IResult From(CommunityServiceException exception) => Problem(exception.Status, exception.Code);
    internal static IResult From(AccountServiceException exception) => Problem(exception.Failure switch
    {
        AccountFailure.InvalidCredentials or AccountFailure.InvalidSession => 401,
        AccountFailure.UsernameUnavailable => 409,
        AccountFailure.SessionNotFound or AccountFailure.ExportNotFound => 404,
        AccountFailure.InvalidRequest => 400,
        AccountFailure.RateLimited => 429,
        AccountFailure.DbUnavailable or AccountFailure.RecoveryUnavailable => 503,
        _ => 500
    }, exception.Code);

    // Version 1 readers reject control characters in Body. Presentation changes only;
    // stored content and modern/native-v2/browser responses retain the original paragraphs.
    private static object? Legacy(object? value) => value switch
    {
        HomeworkResponse h => new HomeworkResponse(h.HomeworkId, h.CommunityId, h.Title, SingleLine(h.Body), h.Revision, h.CreatedAt, h.UpdatedAt),
        AnnouncementResponse a => new AnnouncementResponse(a.AnnouncementId, a.CommunityId, a.Title, SingleLine(a.Body), a.Revision, a.CreatedAt, a.UpdatedAt),
        IEnumerable<HomeworkResponse> homework => homework.Select(h => Legacy(h)).ToArray(),
        IEnumerable<AnnouncementResponse> announcements => announcements.Select(a => Legacy(a)).ToArray(),
        _ => value
    };
    private static string SingleLine(string body) => body.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
    private sealed class TypedJson<T>(T value, int status) : IResult
    {
        public Task ExecuteAsync(HttpContext context)
        {
            object? shown = context.Request.Path.StartsWithSegments("/api/v1/communities") ? Legacy(value) : value;
            return new FrozenJson(CommunityJson.Serialize(shown), status, "application/json; charset=utf-8").ExecuteAsync(context);
        }
    }
    private sealed class FrozenJson(byte[] body, int status, string contentType) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.StatusCode = status;
            context.Response.ContentType = contentType;
            context.Response.ContentLength = body.Length;
            await context.Response.Body.WriteAsync(body, context.RequestAborted);
        }
    }
}
