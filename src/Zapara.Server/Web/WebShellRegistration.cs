namespace Zapara.Server.Web;

public static class WebShellRegistration
{
    public static WebApplication MapWebShell(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Web:Enabled")) return app;
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Value == "/app")
            {
                context.Response.Redirect("/app/");
                return;
            }
            if (context.Request.Path.StartsWithSegments("/app"))
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; worker-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
                if (context.Request.Path.Value?.EndsWith("service-worker.js", StringComparison.Ordinal) == true)
                    context.Response.Headers.CacheControl = "no-cache";
            }
            await next();
        });
        app.UseBlazorFrameworkFiles("/app");
        app.UseStaticFiles();
        app.MapGet("/", () => Results.Redirect("/app/"));
        app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html");
        return app;
    }
}
