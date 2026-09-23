namespace Zapara.Server.Operator;

public sealed record StatusRow(string Name, bool Working, string Detail);

public static class OperatorStatus
{
    public static StatusRow Parser(string? attemptStatus, string? errorCode) => attemptStatus switch
    {
        "running" => new("Парсер расписания", true, "Работает: обновление идёт"),
        "success" => new("Парсер расписания", true, "Работает"),
        "failed" or "abandoned" => new("Парсер расписания", false, "Не работает: " + (string.IsNullOrWhiteSpace(errorCode) ? "сбой парсера" : errorCode)),
        _ => new("Парсер расписания", false, "Не работает: нет попыток обновления"),
    };

    public static StatusRow Schedule(bool hasSnapshot, bool stale, bool failedAfterSuccess, DateTimeOffset? lastSuccess)
    {
        var when = lastSuccess is null ? "успеха ещё не было" : "последний успех " + lastSuccess.Value.ToString("yyyy-MM-dd HH:mm'Z'");
        if (!hasSnapshot) return new("Снимок расписания", false, "Не работает: снимка нет");
        if (failedAfterSuccess) return new("Снимок расписания", false, "Не работает: обновление после успеха не удалось. " + when);
        if (stale) return new("Снимок расписания", false, "Не работает: снимок устарел. " + when);
        return new("Снимок расписания", true, "Свежий. " + when);
    }

    public static StatusRow Probe(string name, string state) => state switch
    {
        "present" => new(name, true, "Работает"),
        "disabled" => new(name, true, "Выключен"),
        "missing" => new(name, false, "Не работает: схема " + name + " отсутствует"),
        _ => new(name, false, "Не работает: " + name),
    };

    public static StatusRow Storage(string state, string? failure = null) => state switch
    {
        "reachable" => new("S3", true, "Доступно"),
        "failing" => new("S3", false, "Не работает: " + (string.IsNullOrWhiteSpace(failure) ? "хранилище не отвечает" : failure)),
        _ => new("S3", false, "Не настроено"),
    };
}
