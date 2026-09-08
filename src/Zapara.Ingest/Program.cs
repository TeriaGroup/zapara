using Microsoft.Extensions.Configuration;
using Zapara.Server.Timetable;

namespace Zapara.Ingest;

public static class Program
{
    public static int Main(string[] args)
    {
        if (!ValidArguments(args)) return CliOutput.InvalidArguments();
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try { return RunConfiguredAsync(args, cancellation.Token).GetAwaiter().GetResult(); }
        catch (ArgumentException) { return CliOutput.ConfigurationRejected(); }
        catch (OperationCanceledException) { return CliOutput.Write(IngestResult.Failed(FailureCode.Cancelled)); }
        catch (StoreException error) { return CliOutput.Write(IngestResult.Failed(error.FailureCode, error.AttemptId)); }
        catch (Exception) { return CliOutput.UnexpectedFailure(); }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static async Task<int> RunConfiguredAsync(string[] args, CancellationToken ct)
    {
        var configuration = LoadConfiguration();
        await using var dataSource = configuration.CreateDataSource();
        var clock = TimeProvider.System;
        var store = new SnapshotStore(dataSource, configuration.Schema, clock, configuration.CreateDedicatedConnection);
        var service = new IngestService(store, new TimetableInput(clock));
        using var http = TimetableInput.CreateHttpClient();
        return await RunAsync(args, store, service, http, ct);
    }

    public static async Task<int> RunAsync(string[] args, SnapshotStore store, IngestService service,
        HttpClient http, CancellationToken ct = default)
    {
        if (!ValidArguments(args)) return CliOutput.InvalidArguments();
        try
        {
            if (args[0] == "db-init")
            {
                await store.EnsureSchemaAsync(ct);
                return CliOutput.Write(new IngestResult(0));
            }
            var result = args[1] == "--fetch"
                ? await service.IngestFetchResultAsync(http, ct)
                : await service.IngestFileResultAsync(args[2], ct);
            return CliOutput.Write(result);
        }
        catch (StoreException error) { return CliOutput.Write(IngestResult.Failed(error.FailureCode, error.AttemptId)); }
        catch (OperationCanceledException) { return CliOutput.Write(IngestResult.Failed(FailureCode.Cancelled)); }
    }

    private static bool ValidArguments(string[] args) => args is ["db-init"] or ["ingest", "--fetch"]
        || args is ["ingest", "--file", var path] && !string.IsNullOrWhiteSpace(path)
            && !path.StartsWith("--", StringComparison.Ordinal);

    public static TimetableConfiguration LoadConfiguration() => TimetableConfiguration.FromConfiguration(
        new ConfigurationBuilder().AddEnvironmentVariables().Build());
}
