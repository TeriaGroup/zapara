using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Zapara.Server.Accounts.ExternalProviders;
using Zapara.Server.Operator;

namespace Zapara.Server.Accounts;

public static class ExternalAuthRegistration
{
    public static IServiceCollection AddExternalAuth(this IServiceCollection services, IConfiguration configuration)
    {
        if (!AccountsConfiguration.IsEnabled(configuration)) return services;
        services.AddSingleton(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            return new ExternalProviderRegistry([], new Dictionary<string, string>(), () => Stamp(config), () => Build(config));
        });
        services.AddSingleton<ExternalAuthService>();
        services.AddHostedService<ExternalAuthCleanup>();
        return services;
    }

    private static (IReadOnlyList<IExternalProviderAdapter> Adapters, IReadOnlyDictionary<string, string> Callbacks) Build(IConfiguration config)
    {
        var stored = ReadOperator(config);
        var callbacks = new Dictionary<string, string>(StringComparer.Ordinal);
        var adapters = new List<IExternalProviderAdapter>();
        foreach (var name in new[] { "vk", "yandex" })
        {
            var section = config.GetSection("Accounts:External:" + name);
            var sectionReady = bool.TryParse(section["Enabled"], out var flag) && flag
                && !string.IsNullOrWhiteSpace(section["ClientId"]) && !string.IsNullOrWhiteSpace(section["CallbackUri"]);
            if (!OperatorConfig.ProviderAvailable(stored, name, sectionReady)) continue;
            var clientId = OperatorConfig.Pick(stored, name + "_client_id", section["ClientId"]);
            var callback = OperatorConfig.Pick(stored, name + "_callback", section["CallbackUri"]);
            var secret = OperatorConfig.Pick(stored, name + "_secret", name == "vk" ? section["ServiceToken"] : section["ClientSecret"]);
            try
            {
                IExternalProviderAdapter adapter = name == "vk"
                    ? new VkIdAdapter(new(clientId, callback, secret))
                    : new YandexIdAdapter(new(clientId, callback, secret));
                adapters.Add(adapter);
                callbacks.Add(name, callback!);
            }
            catch (ExternalProviderException) { /* Invalid operator metadata leaves this method unavailable. */ }
        }
        return (adapters, callbacks);
    }

    private static string Stamp(IConfiguration config)
    {
        var stored = ReadOperator(config);
        var parts = new List<string>();
        foreach (var name in new[] { "vk", "yandex" })
        {
            var section = config.GetSection("Accounts:External:" + name);
            parts.Add(name);
            parts.Add(OperatorConfig.Enabled(stored, name + "_enabled", bool.TryParse(section["Enabled"], out var flag) && flag) ? "1" : "0");
            parts.Add(OperatorConfig.Pick(stored, name + "_client_id", section["ClientId"]) ?? "");
            parts.Add(OperatorConfig.Pick(stored, name + "_callback", section["CallbackUri"]) ?? "");
            var secret = OperatorConfig.Pick(stored, name + "_secret", name == "vk" ? section["ServiceToken"] : section["ClientSecret"]) ?? "";
            parts.Add(secret.Length == 0 ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        }
        return string.Join("\n", parts);
    }

    private static IReadOnlyDictionary<string, string> ReadOperator(IConfiguration configuration)
    {
        try
        {
            var raw = configuration.GetConnectionString("Accounts") ?? configuration.GetConnectionString("Timetable");
            if (string.IsNullOrWhiteSpace(raw)) return new Dictionary<string, string>();
            using var connection = new NpgsqlConnection(raw);
            return OperatorSettings.ReadAll(connection, OperatorSettings.Schema(configuration));
        }
        catch (Exception) { return new Dictionary<string, string>(); }
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
