using Microsoft.AspNetCore.Http;
using Zapara.Contracts.Communities;
using Zapara.Server.Accounts;

namespace Zapara.Server.Communities;

internal static class CommunityHttpResult
{
    internal static IResult Json<T>(T value, int status = 200) => new FrozenJson(CommunityJson.Serialize(value), status, "application/json; charset=utf-8");
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
        AccountFailure.InvalidRequest => 400,
        AccountFailure.RateLimited => 429,
        AccountFailure.DbUnavailable => 503,
        _ => 500
    }, exception.Code);

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
