using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Zapara.Server.Accounts;

namespace Zapara.Server.Admin;

public static class AdminRegistration
{
    public static IServiceCollection AddAdmin(this IServiceCollection services, IConfiguration configuration)
    {
        if (!AdminConfiguration.IsEnabled(configuration)) return services;
        if (!AccountsConfiguration.IsEnabled(configuration)) throw new ArgumentException("Admin требует Accounts.");
        if (!Zapara.Server.Communities.CommunitiesConfiguration.IsEnabled(configuration))
            throw new ArgumentException("Admin требует Communities.");
        services.AddSingleton(provider =>
        {
            try { return AdminConfiguration.FromConfiguration(provider.GetRequiredService<IConfiguration>()); }
            catch (ArgumentException) { throw new AccountServiceException(AccountFailure.DbUnavailable); }
        });
        services.AddSingleton<AdminAuthService>();
        services.AddSingleton<AdminService>();
        services.AddSingleton<ITicketStore, AdminTicketStore>();
        services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, AdminCookiePostConfigure>();
        var keys = configuration["Admin:DataProtectionKeysPath"];
        if (!string.IsNullOrWhiteSpace(keys))
        {
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keys))
                .SetApplicationName("Zapara.Admin");
        }
        services.AddAuthentication()
            .AddCookie(AdminDefaults.Scheme, options =>
            {
                options.Cookie.Name = AdminDefaults.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.IsEssential = true;
                options.ExpireTimeSpan = AdminDefaults.SessionLifetime;
                options.SlidingExpiration = false;
                options.LoginPath = "/Admin/Login";
                options.LogoutPath = "/Admin/Logout";
                options.AccessDeniedPath = "/Admin/Login";
                options.ReturnUrlParameter = "returnUrl";
            });
        services.AddAuthorization(options => options.AddPolicy(AdminDefaults.Policy, policy =>
            policy.AddAuthenticationSchemes(AdminDefaults.Scheme).RequireAuthenticatedUser().RequireClaim("admin", "platform")));
        services.AddRazorPages(options =>
        {
            options.Conventions.AuthorizeFolder("/Admin", AdminDefaults.Policy);
            options.Conventions.AllowAnonymousToPage("/Admin/Login");
        }).AddMvcOptions(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
            .AddViewOptions(options => options.HtmlHelperOptions.ClientValidationEnabled = false);
        services.AddAntiforgery(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.HeaderName = "RequestVerificationToken";
        });
        return services;
    }

    public static WebApplication MapAdmin(this WebApplication app)
    {
        if (!AdminConfiguration.IsEnabled(app.Configuration)) return app;
        app.UseStaticFiles();
        app.UseAntiforgery();
        app.Map("/Admin", () => Results.NotFound());
        app.Map("/Admin/{**rest}", () => Results.NotFound());
        return app;
    }
}

internal sealed class AdminCookiePostConfigure(ITicketStore store) : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name == AdminDefaults.Scheme) options.SessionStore = store;
    }
}
