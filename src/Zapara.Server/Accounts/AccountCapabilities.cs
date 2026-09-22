using Zapara.Contracts.Accounts.ExternalResponses;

namespace Zapara.Server.Accounts;

public static class AccountCapabilities
{
    public static bool RegistrationEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        var raw = configuration["Accounts:RegistrationEnabled"];
        if (raw is null) return environment.IsDevelopment() || environment.IsEnvironment("Testing");
        return bool.TryParse(raw, out var enabled) && enabled;
    }

    public static AuthCapabilitiesResponse Read(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        if (!AccountsConfiguration.IsEnabled(config)) return new(false, false, false, false, false);
        var registry = services.GetService<ExternalProviderRegistry>();
        return new(true, registry?.IsConfigured("vk") == true, registry?.IsConfigured("yandex") == true,
            RegistrationEnabled(config, services.GetRequiredService<IHostEnvironment>()),
            services.GetService<RecoveryService>()?.RecoveryEnabled == true);
    }
}
