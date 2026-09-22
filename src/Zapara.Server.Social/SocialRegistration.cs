using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zapara.Server.Accounts;

namespace Zapara.Server.Social;

public static class SocialRegistration
{
    public static IServiceCollection AddSocial(this IServiceCollection services, IConfiguration configuration)
    {
        if (!AccountsConfiguration.IsEnabled(configuration)) return services;
        services.AddSingleton(provider =>
        {
            try { return SocialConfiguration.Create(provider.GetRequiredService<AccountsConfiguration>(), provider.GetRequiredService<IConfiguration>()); }
            catch (ArgumentException) { throw new AccountServiceException(AccountFailure.DbUnavailable); }
        });
        services.AddSingleton(provider => new MediaStore(provider.GetRequiredService<SocialConfiguration>().MediaRoot));
        services.AddSingleton<IAccountUnitOfWork>(provider => provider.GetRequiredService<AccountService>());
        services.AddSingleton<SocialService>();
        services.AddHostedService<SocialSchemaService>();
        services.AddHostedService<SocialFileSweeper>();
        services.AddSingleton<IAccountLifecycleParticipant, SocialLifecycleParticipant>();
        return services;
    }
}
