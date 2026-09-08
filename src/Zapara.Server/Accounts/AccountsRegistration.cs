using Microsoft.AspNetCore.Authentication;
using Npgsql;

namespace Zapara.Server.Accounts;

public static class AccountsRegistration
{
    public static IServiceCollection AddAccounts(this IServiceCollection services, IConfiguration configuration)
    {
        if (!AccountsConfiguration.IsEnabled(configuration)) return services;
        services.AddSingleton(provider =>
        {
            try { return AccountsConfiguration.FromConfiguration(provider.GetRequiredService<IConfiguration>()); }
            catch (ArgumentException) { throw new AccountServiceException(AccountFailure.DbUnavailable); }
        });
        services.AddSingleton(provider =>
        {
            try { return provider.GetRequiredService<AccountsConfiguration>().CreateDataSource(); }
            catch (Exception exception) when (exception is ArgumentException or NpgsqlException or TimeoutException)
            { throw new AccountServiceException(AccountFailure.DbUnavailable); }
        });
        services.AddSingleton<AccountPasswordWork>();
        services.AddSingleton<AccountService>();
        services.AddSingleton<IRecoveryDelivery>(provider =>
        {
            var environment = provider.GetRequiredService<IHostEnvironment>();
            return environment.IsDevelopment() || environment.IsEnvironment("Testing")
                ? new TestingRecoverySink()
                : UnconfiguredRecoveryDelivery.Instance;
        });
        services.AddSingleton<RecoveryService>();
        services.AddSingleton<IAccountLifecycleParticipant, AccountLifecycleParticipant>();
        services.AddSingleton<AccountLifecycleService>();
        services.AddAuthentication(OpaqueAccountHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, OpaqueAccountHandler>(OpaqueAccountHandler.SchemeName, _ => { });
        services.AddAuthorization(options => options.AddPolicy("AccountUser", policy =>
            policy.AddAuthenticationSchemes(OpaqueAccountHandler.SchemeName).RequireAuthenticatedUser()));
        AccountRateLimits.Add(services);
        return services;
    }

    public static WebApplication MapAccounts(this WebApplication app)
    {
        if (!AccountsConfiguration.IsEnabled(app.Configuration)) return app;
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<AccountEndpoint>() is not null)
                context.Response.Headers.CacheControl = "no-store";
            if (context.GetEndpoint()?.Metadata.GetMetadata<ExternalEndpoint>() is not null)
            {
                var size = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if (size is { IsReadOnly: false }) size.MaxRequestBodySize = AccountBodyReader.MaximumBytes;
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            }
            await next(context);
        });
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        AccountEndpoints.Map(app);
        return app;
    }
}

internal sealed record AccountEndpoint;
