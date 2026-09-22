using Zapara.Server.Timetable;

namespace Zapara.Server.Web;

public static class TimetableRefreshRegistration
{
    public static IServiceCollection AddTimetableRefresh(this IServiceCollection services, IConfiguration configuration)
    {
        var policy = TimetableRefreshPolicy.FromConfiguration(configuration);
        if (!policy.Enabled) return services;
        services.AddSingleton(policy);
        services.AddSingleton<TimetableInput>();
        services.AddSingleton<IngestService>();
        services.AddHostedService<TimetableRefreshWorker>();
        return services;
    }
}

internal sealed class TimetableRefreshWorker(TimetableRefreshPolicy policy, IngestService ingest,
    TimeProvider clock, ILogger<TimetableRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = TimetableInput.CreateHttpClient();
        try
        {
            await policy.RunAsync(ct => ingest.IngestRefreshResultAsync(client, clock, ct),
                (delay, ct) => Task.Delay(delay, clock, ct), result =>
                {
                    if (result.ExitCode == 0) logger.LogInformation("Расписание обновлено: {SnapshotId}", result.SnapshotId);
                    else if (result.ExitCode != 3) logger.LogWarning("Обновление расписания: {FailureCode}", result.FailureCode?.ToStorageCode());
                }, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
