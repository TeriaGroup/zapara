using Zapara.Server.Accounts.ExternalProviders;

namespace Zapara.Server.Accounts;

public static class ExternalAuthRegistration
{
    public static IServiceCollection AddExternalAuth(this IServiceCollection services, IConfiguration configuration)
    {
        if (!AccountsConfiguration.IsEnabled(configuration)) return services;
        services.AddSingleton(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var callbacks = new Dictionary<string, string>();
            var adapters = new List<IExternalProviderAdapter>();
            foreach (var name in new[] { "vk", "yandex" })
            {
                var section = config.GetSection("Accounts:External:" + name);
                if (!bool.TryParse(section["Enabled"], out var enabled) || !enabled ||
                    string.IsNullOrWhiteSpace(section["ClientId"]) || string.IsNullOrWhiteSpace(section["CallbackUri"])) continue;
                try
                {
                    IExternalProviderAdapter adapter = name == "vk"
                        ? new VkIdAdapter(new(section["ClientId"], section["CallbackUri"], section["ServiceToken"]))
                        : new YandexIdAdapter(new(section["ClientId"], section["CallbackUri"], section["ClientSecret"]));
                    adapters.Add(adapter);
                    callbacks.Add(name, section["CallbackUri"]!);
                }
                catch (ExternalProviderException) { /* Invalid operator metadata leaves this method unavailable. */ }
            }
            return new ExternalProviderRegistry(adapters, callbacks);
        });
        services.AddSingleton<ExternalAuthService>();
        services.AddHostedService<ExternalAuthCleanup>();
        return services;
    }

    public static WebApplication MapExternalAuth(this WebApplication app)
    {
        if (AccountsConfiguration.IsEnabled(app.Configuration)) ExternalAuthEndpoints.Map(app);
        return app;
    }
}

internal sealed class ExternalAuthCleanup(IServiceProvider services, ILogger<ExternalAuthCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await services.GetRequiredService<ExternalAuthService>().CleanupAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (AccountServiceException) { logger.LogWarning("Очистка временных данных входа недоступна."); }
        }
    }
}
