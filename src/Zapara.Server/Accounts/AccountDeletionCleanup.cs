namespace Zapara.Server.Accounts;

internal sealed class AccountDeletionCleanup(IServiceProvider services, IHostApplicationLifetime lifetime,
    TimeProvider clock, ILogger<AccountDeletionCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        try
        {
            // Lifecycle participants may have their own schema startup services.
            await started.Task.WaitAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
            do
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                try { await services.GetRequiredService<AccountLifecycleService>().ProcessPendingDeletionsAsync(deadline.Token); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception) { logger.LogWarning("Фоновая очистка аккаунтов временно недоступна."); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
