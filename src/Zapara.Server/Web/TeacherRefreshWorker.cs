namespace Zapara.Server.Web;

public sealed record TeacherRefreshPolicy(bool Enabled, TimeSpan Interval, TimeSpan RetryBase, TimeSpan RetryMaximum, TimeSpan Deadline)
{
    public static TeacherRefreshPolicy FromConfiguration(IConfiguration configuration)
    {
        var value = configuration["Teachers:Refresh:Enabled"];
        if (value is not null && !bool.TryParse(value, out _)) throw new ArgumentException("Недопустимый Teachers:Refresh:Enabled.");
        var enabled = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        var retry = Number("RetrySeconds", 30, 5, 300);
        return new(enabled, TimeSpan.FromMinutes(Number("IntervalMinutes", 60, 15, 1440)), TimeSpan.FromSeconds(retry),
            TimeSpan.FromSeconds(Number("MaximumRetrySeconds", 900, retry, 3600)), TimeSpan.FromSeconds(Number("DeadlineSeconds", 30, 1, 120)));

        int Number(string key, int fallback, int min, int max)
        {
            var raw = configuration["Teachers:Refresh:" + key];
            if (raw is null) return fallback;
            if (!int.TryParse(raw, out var parsed) || parsed < min || parsed > max) throw new ArgumentException("Недопустимый Teachers:Refresh:" + key + ".");
            return parsed;
        }
    }

    public async Task RunAsync(Func<CancellationToken, Task<bool>> refresh, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct)
    {
        if (!Enabled) return;
        var wait = TimeSpan.FromSeconds(5);
        var failures = 0;
        while (true)
        {
            await delay(wait, ct);
            ct.ThrowIfCancellationRequested();
            var success = await refresh(ct);
            failures = success ? 0 : Math.Min(failures + 1, 21);
            wait = success ? Interval : TimeSpan.FromSeconds(Math.Min(RetryMaximum.TotalSeconds,
                RetryBase.TotalSeconds * Math.Pow(2, failures - 1)));
        }
    }
}

public sealed class TeacherRefreshWorker(TeacherCatalogStore store, TeacherRefreshPolicy policy, ILogger<TeacherRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!policy.Enabled) return;
        using var client = TeacherCatalogInput.CreateHttpClient();
        try
        {
            await policy.RunAsync(async token =>
            {
                var success = await store.RefreshAsync(client, policy.Deadline, token);
                if (!success) logger.LogWarning("Обновление каталога преподавателей не выполнено: {FailureCode}.", store.Capture().Metadata.LastFailure ?? "busy");
                return success;
            }, (wait, token) => Task.Delay(wait, token), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
