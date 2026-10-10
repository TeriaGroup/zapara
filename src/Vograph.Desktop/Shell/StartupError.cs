using System.Runtime.InteropServices;

namespace Vograph.Desktop.Shell;

/// <summary>What the startup error window shows when AppServices.Create failed (a locked or corrupt database, an unwritable data folder).</summary>
public sealed record StartupError(string Message, string DataDir, string LogFile, string? Details = null)
{
    /// <summary>Retry attempt that failed again — the window says so instead of silently showing the same text.</summary>
    public int Attempt { get; init; }
    public StartupErrorKind Kind => StartupErrorCatalog.Classify(Details ?? Message);
    public string Title => StartupErrorCatalog.Title(Kind);
    public string Explanation => StartupErrorCatalog.Explanation(Kind);
    public string RetryNote => Attempt > 0 ? "Повторная попытка не помогла. Ошибка та же." : "";
    public bool HasRetryNote => Attempt > 0;

    public static StartupError From(Exception error, string dataDir, string logFile, int attempt = 0) =>
        new(error.Message, dataDir, logFile, $"{error.GetType().FullName}: {error.Message}") { Attempt = attempt };
}

public enum StartupErrorKind { Unknown, AlreadyRunning, NoAccess, DiskFull, Corrupt, DataPathIsFile }

/// <summary>
/// Известные ошибки запуска → понятный русский текст с действием (#18, D-01). Исходное сообщение остаётся под «Подробности»
/// и в отчёте. Классификация — по тексту исключения (SQLite и .NET пишут коды в сообщение).
/// </summary>
public static class StartupErrorCatalog
{
    public static StartupErrorKind Classify(string text)
    {
        var t = text ?? "";
        bool Has(params string[] parts) => parts.Any(part => t.Contains(part, StringComparison.OrdinalIgnoreCase));
        if (Has("SQLite Error 5:", "SQLite Error 6:", "database is locked", "database table is locked", "being used by another process"))
            return StartupErrorKind.AlreadyRunning;
        if (Has("SQLite Error 13:", "database or disk is full", "not enough space", "No space left"))
            return StartupErrorKind.DiskFull;
        if (Has("SQLite Error 11:", "SQLite Error 26:", "malformed", "file is not a database"))
            return StartupErrorKind.Corrupt;
        // #39: на Linux без HOME путь становится относительным «Vograph» и указывает на сам исполняемый файл.
        if (Has("IOException") && Has("already exists", "Not a directory"))
            return StartupErrorKind.DataPathIsFile;
        if (Has("SQLite Error 14:", "SQLite Error 8:", "unable to open database file", "readonly database", "UnauthorizedAccessException", "Access to the path", "Permission denied"))
            return StartupErrorKind.NoAccess;
        return StartupErrorKind.Unknown;
    }

    public static string Title(StartupErrorKind kind) => kind switch
    {
        StartupErrorKind.AlreadyRunning => "Военмех уже запущен?",
        StartupErrorKind.NoAccess => "Нет доступа к папке с данными",
        StartupErrorKind.DiskFull => "На диске нет места",
        StartupErrorKind.Corrupt => "Файл с данными повреждён",
        StartupErrorKind.DataPathIsFile => "Не удалось создать папку с данными",
        _ => "Приложение не запустилось",
    };

    public static string Explanation(StartupErrorKind kind) => kind switch
    {
        StartupErrorKind.AlreadyRunning => "Похоже, приложение уже запущено или прошлое обновление не завершилось. Закройте другие окна «Военмеха» и нажмите «Повторить».",
        StartupErrorKind.NoAccess => "Приложению не удалось открыть свою папку с данными. Проверьте, что диск подключён и папка не защищена от записи, затем нажмите «Повторить».",
        StartupErrorKind.DiskFull => "Приложению негде сохранить данные. Освободите место на диске и нажмите «Повторить».",
        StartupErrorKind.Corrupt => "Сохранённые данные приложения повреждены. Нажмите «Скопировать отчёт» и отправьте его в поддержку — мы подскажем, как восстановить данные.",
        StartupErrorKind.DataPathIsFile => $"На месте папки с данными лежит файл с тем же именем. Запустите приложение из другой папки или укажите папку для данных в переменной {Services.AppPaths.DataDirEnv} и нажмите «Повторить».",
        _ => "Что-то помешало запуску. Нажмите «Повторить». Если ошибка повторится, скопируйте отчёт и отправьте его в поддержку.",
    };

    /// <summary>Текст для «Скопировать отчёт»: версия, система, время, исходная ошибка и путь к журналу. Без личных данных, кроме пути к журналу.</summary>
    public static string Report(StartupError error, string version, DateTimeOffset now) => string.Join(Environment.NewLine,
        $"Военмех {version} — ошибка запуска",
        $"Время: {now:yyyy-MM-dd HH:mm:ss zzz}",
        $"Система: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
        $"Тип: {error.Kind}",
        $"Ошибка: {error.Details ?? error.Message}",
        $"Журнал: {error.LogFile}");
}
