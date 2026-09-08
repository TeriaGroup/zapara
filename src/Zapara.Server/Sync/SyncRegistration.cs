using Zapara.Server.Accounts;

namespace Zapara.Server.Sync;

public static class SyncRegistration
{
    public static IServiceCollection AddSync(this IServiceCollection services, IConfiguration configuration)
    {
        if (!SyncConfiguration.IsEnabled(configuration)) return services;
        if (!AccountsConfiguration.IsEnabled(configuration)) throw new ArgumentException("Sync требует Accounts.");
        services.AddSingleton(provider =>
        {
            // Resolve the existing Accounts configuration first; never register another datasource.
            var accounts = provider.GetRequiredService<AccountsConfiguration>();
            try
            {
                var sync = SyncConfiguration.FromConfiguration(provider.GetRequiredService<IConfiguration>());
                if (sync.Accounts.Schema != accounts.Schema) throw new ArgumentException();
                return sync;
            }
            catch (ArgumentException) { throw new AccountServiceException(AccountFailure.DbUnavailable); }
        });
        services.AddSingleton<IAccountUnitOfWork>(provider => provider.GetRequiredService<AccountService>());
        services.AddSingleton<SyncService>();
        services.AddSingleton<IAccountLifecycleParticipant, SyncLifecycleParticipant>();
        return services;
    }

    public static WebApplication MapSync(this WebApplication app)
    {
        if (SyncConfiguration.IsEnabled(app.Configuration)) SyncEndpoints.Map(app);
        return app;
    }
}
