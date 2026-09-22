using Microsoft.AspNetCore.DataProtection;
using Zapara.Server.Accounts;
using Zapara.Server.Notifications;

namespace Zapara.Server.Web;

public static class WebRegistration
{
    public static IServiceCollection AddWebClient(this IServiceCollection services, IConfiguration configuration)
    {
        if (!WebConfiguration.Enabled(configuration)) return services;
        services.AddDataProtection(); // No application-name/key-ring override: admin isolation is preserved.
        services.AddKeyedSingleton<IDataProtectionProvider>(WebProtection.Key, (provider, _) => WebProtection.Create(provider));
        services.AddSingleton<WebBrowserState>();
        if (AccountsConfiguration.IsEnabled(configuration))
        {
            services.AddSingleton<WebSessionStore>();
            services.AddSingleton<WebOAuth>();
            services.AddHostedService<WebSessionCleanup>();
            services.AddNotifications(configuration);
        }
        return services;
    }

    public static WebApplication MapWebClient(this WebApplication app)
    {
        if (!WebConfiguration.Enabled(app.Configuration)) return app;
        WebEndpoints.Map(app);
        return app;
    }
}
