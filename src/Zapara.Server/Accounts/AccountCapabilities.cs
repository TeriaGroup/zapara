using Npgsql;
using Zapara.Contracts.Accounts.ExternalResponses;

namespace Zapara.Server.Accounts;

public static class AccountCapabilities
{
    public static bool RegistrationEnabled(IConfiguration configuration, IHostEnvironment environment, NpgsqlConnection? connection = null)
    {
        if (connection is not null && OperatorSettings.TryReadRegistration(connection, OperatorSettings.Schema(configuration), out var stored))
            return stored;
        var raw = configuration["Accounts:RegistrationEnabled"];
        if (raw is null) return environment.IsDevelopment() || environment.IsEnvironment("Testing");
        return bool.TryParse(raw, out var enabled) && enabled;
    }

    public static bool RegistrationEnabled(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IHostEnvironment>();
        var source = services.GetService<AccountsDataSource>();
        if (source is null) return RegistrationEnabled(config, environment);
        try
        {
            using var connection = source.CreateConnection();
            return RegistrationEnabled(config, environment, connection);
        }
        catch (NpgsqlException)
        {
            return RegistrationEnabled(config, environment);
        }
    }

    public static AuthCapabilitiesResponse Read(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        if (!AccountsConfiguration.IsEnabled(config)) return new(false, false, false, false, false);
        var registry = services.GetService<ExternalProviderRegistry>();
        return new(true, registry?.IsConfigured("vk") == true, registry?.IsConfigured("yandex") == true,
            RegistrationEnabled(services),
            services.GetService<RecoveryService>()?.RecoveryEnabled == true);
    }
}
