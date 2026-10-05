using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Zapara.Server.Notifications;

public static class NotificationsRegistration
{
    public static IServiceCollection AddNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(provider => PushConfiguration.Read(provider.GetRequiredService<IConfiguration>()));
        services.TryAddSingleton<IPushTransport>(provider => provider.GetRequiredService<PushConfiguration>().Available
            ? new PushTransport(provider.GetRequiredService<PushConfiguration>()) : new UnconfiguredPushTransport());
        services.AddSingleton<PushSubscriptionService>();
        services.AddHostedService<PushWorker>();
        return services;
    }
}

internal sealed class PushWorker(IServiceProvider services, ILogger<PushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await services.GetRequiredService<PushSubscriptionService>().RunScheduledAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("Доставка уведомлений временно недоступна."); }
        }
    }
}

internal sealed class UnconfiguredPushTransport : IPushTransport
{
    public Task<PushDeliveryOutcome> SendAsync(PushSubscriptionRequest subscription, CancellationToken ct)
        => Task.FromResult(PushDeliveryOutcome.Unavailable);
}
