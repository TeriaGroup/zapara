namespace Zapara.Server;

internal static class ApiErrors
{
    internal static IResult InvalidSnapshotId() => Problem(400, "Некорректный идентификатор снимка", "invalid_snapshot_id");
    internal static IResult SnapshotNotFound() => Problem(404, "Снимок не найден", "snapshot_not_found");
    internal static IResult GroupNotFound() => Problem(404, "Группа не найдена", "group_not_found");
    internal static IResult SnapshotUnavailable() => Problem(503, "Расписание пока недоступно", "snapshot_unavailable");
    internal static IResult DatabaseUnavailable() => Problem(503, "Хранилище временно недоступно", "db_unavailable");
    internal static IResult InternalError() => Problem(500, "Внутренняя ошибка сервера", "internal_error");

    internal static IResult ForStatus(int status, string code) => Problem(status, status switch
    {
        400 or 413 or 414 or 415 or 431 => "Некорректный запрос",
        401 => "Не удалось подтвердить доступ",
        403 => "Доступ запрещён",
        404 => "Ресурс не найден",
        409 => "Конфликт изменения",
        429 => "Слишком много запросов",
        503 => "Сервис временно недоступен",
        _ => "Некорректный запрос"
    }, code);

    private static IResult Problem(int status, string title, string code)
        => Results.Json(new { title, status, code }, statusCode: status, contentType: "application/problem+json");
}
