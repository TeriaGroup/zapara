using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zapara.Server.Accounts;

namespace Zapara.Server.Communities;

public static class CommunitiesRegistration
{
    public static IServiceCollection AddCommunities(this IServiceCollection services, IConfiguration configuration)
    {
        if (!CommunitiesConfiguration.IsEnabled(configuration)) return services;
        if (!AccountsConfiguration.IsEnabled(configuration)) throw new ArgumentException("Communities требуют Accounts.");
        services.AddSingleton(provider =>
        {
            var accounts = provider.GetRequiredService<AccountsConfiguration>();
            try
            {
                var communities = CommunitiesConfiguration.FromConfiguration(provider.GetRequiredService<IConfiguration>());
                if (communities.Accounts.Schema != accounts.Schema) throw new ArgumentException();
                return communities;
            }
            catch (ArgumentException) { throw new AccountServiceException(AccountFailure.DbUnavailable); }
        });
        services.AddSingleton<IAccountUnitOfWork>(provider => provider.GetRequiredService<AccountService>());
        services.AddSingleton<CommunityService>();
        services.AddSingleton<IAccountLifecycleParticipant, CommunityLifecycleParticipant>();
        return services;
    }

    public static WebApplication MapCommunities(this WebApplication app)
    {
        if (CommunitiesConfiguration.IsEnabled(app.Configuration)) CommunityEndpoints.Map(app);
        return app;
    }
}
