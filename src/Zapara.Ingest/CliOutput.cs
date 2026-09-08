using System.Text.Json;
using Zapara.Server.Timetable;

namespace Zapara.Ingest;

internal static class CliOutput
{
    public static int InvalidArguments() => Error("invalid_arguments", 2);
    public static int ConfigurationRejected() => Error("invalid_configuration", 2);
    public static int UnexpectedFailure() => Error("internal_error", 5);

    private static int Error(string code, int exitCode)
    {
        Console.Error.WriteLine($"Операция не выполнена. Код: {code}; код выхода: {exitCode}.");
        return exitCode;
    }

    public static int Write(IngestResult result)
    {
        var code = result.FailureCode?.ToStorageCode() ?? (result.ExitCode == 3 ? "busy" : null);
        if (result.ExitCode != 0)
        {
            Error(code ?? "internal_error", result.ExitCode);
            if (result.AttemptId is { } attempt) Console.Error.WriteLine($"Попытка: {attempt:D}.");
            if (result.FailureCode == FailureCode.PublicationUnknown)
                Console.Error.WriteLine("Результат публикации неизвестен. Проверьте попытку по её идентификатору в базе данных; не повторяйте публикацию автоматически.");
        }
        if (result.CleanupFailureCode is { } cleanup)
            Console.Error.WriteLine($"Не удалось завершить очистку или запись ошибки. Код: {cleanup.ToStorageCode()}; попытка: {result.AttemptId:D}.");
        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            outcome = result.ExitCode == 0 ? "success" : result.FailureCode == FailureCode.PublicationUnknown ? "unknown" : "failed",
            attemptId = result.AttemptId,
            snapshotId = result.SnapshotId,
            counts = result.Counts,
            code
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return result.ExitCode;
    }
}
