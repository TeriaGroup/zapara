using Zapara.Contracts.Accounts;

namespace Zapara.Server.Accounts;

internal static class AccountErrors
{
    internal static IResult From(AccountServiceException exception) => Problem(exception.Failure switch
    {
        AccountFailure.InvalidCredentials or AccountFailure.InvalidSession => 401,
        AccountFailure.UsernameUnavailable => 409,
        AccountFailure.SessionNotFound => 404,
        AccountFailure.InvalidRequest => 400,
        AccountFailure.RateLimited => 429,
        AccountFailure.DbUnavailable => 503,
        AccountFailure.ExportNotFound => 404,
        _ => 500
    }, exception.Code);

    internal static IResult Problem(int status, string code) => Results.Json(
        new AccountError(status switch
        {
            400 or 413 or 415 => "Некорректный запрос",
            401 => "Не удалось подтвердить доступ",
            403 => "Доступ запрещён",
            404 => code == "export_not_found" ? "Экспорт не найден" : "Сессия не найдена",
            409 => "Имя пользователя недоступно",
            429 => "Слишком много запросов",
            503 => "Сервис временно недоступен",
            _ => "Внутренняя ошибка сервера"
        }, status, code), AccountJson.CreateOptions(), "application/problem+json", status);

    internal static Task Write(HttpContext context, int status, string code)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (status == 429 && !context.Response.Headers.ContainsKey("Retry-After"))
            context.Response.Headers.RetryAfter = "60";
        return Problem(status, code).ExecuteAsync(context);
    }
}

internal sealed class AccountBodyException(int status = 400) : Exception("Некорректный запрос")
{
    internal int Status { get; } = status;
}
