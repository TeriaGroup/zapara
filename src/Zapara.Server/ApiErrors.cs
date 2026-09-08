namespace Zapara.Server;

internal static class ApiErrors
{
    internal static IResult InvalidSnapshotId() => Problem(400, "Некорректный идентификатор снимка", "invalid_snapshot_id");
    internal static IResult SnapshotNotFound() => Problem(404, "Снимок не найден", "snapshot_not_found");
    internal static IResult GroupNotFound() => Problem(404, "Группа не найдена", "group_not_found");
    internal static IResult SnapshotUnavailable() => Problem(503, "Расписание пока недоступно", "snapshot_unavailable");
    internal static IResult DatabaseUnavailable() => Problem(503, "Хранилище временно недоступно", "db_unavailable");
    internal static IResult InternalError() => Problem(500, "Внутренняя ошибка сервера", "internal_error");

    private static IResult Problem(int status, string title, string code)
        => Results.Json(new { title, status, code }, statusCode: status, contentType: "application/problem+json");
}
