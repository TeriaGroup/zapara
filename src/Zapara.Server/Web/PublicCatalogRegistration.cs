namespace Zapara.Server.Web;

public static class PublicCatalogRegistration
{
    public static IServiceCollection AddPublicCatalogs(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var environment = provider.GetRequiredService<IWebHostEnvironment>();
            var root = Path.Combine(environment.ContentRootPath, "public-data");
            if (!Directory.Exists(root)) root = Path.Combine(AppContext.BaseDirectory, "public-data");
            // Choose one complete package, never mix files across versions or fetch arbitrary paths.
            return new PackagedPublicCatalog(root);
        });
        services.AddSingleton(provider => new TeacherCatalogStore(provider.GetRequiredService<PackagedPublicCatalog>().TeacherSnapshot,
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddSingleton(provider => TeacherRefreshPolicy.FromConfiguration(provider.GetRequiredService<IConfiguration>()));
        services.AddHostedService<TeacherRefreshWorker>();
        return services;
    }

    public static WebApplication MapPublicCatalogs(this WebApplication app)
    {
        var catalog = app.Services.GetRequiredService<PackagedPublicCatalog>();
        var teachers = app.Services.GetRequiredService<TeacherCatalogStore>();
        app.MapGet("/api/v1/teachers", (HttpContext context) =>
        {
            var snapshot = teachers.Capture();
            return Cached(context, snapshot.RepresentationVersion, 60) ? Results.StatusCode(304) : Results.Json(snapshot.Teachers);
        });
        app.MapGet("/api/v1/teachers/{id}/timetable", (HttpContext context, string id) =>
        {
            var snapshot = teachers.Capture();
            var value = snapshot.Teacher(id);
            if (value is null) return Results.NotFound(new { error = "teacher_not_found" });
            return Cached(context, snapshot.RepresentationVersion, 60) ? Results.StatusCode(304) : Results.Json(value);
        });
        app.MapGet("/api/v1/maps/manifest", (HttpContext context) =>
            Cached(context, catalog.Maps.Version) ? Results.StatusCode(304) : Results.Json(catalog.Maps));
        app.MapGet("/api/v1/maps/assets/{filename}", (HttpContext context, string filename) =>
        {
            var asset = catalog.Asset(filename);
            if (asset is null) return Results.NotFound(new { error = "map_not_found" });
            return Cached(context, asset.Sha256) ? Results.StatusCode(304) : Results.Bytes(asset.Content, asset.ContentType);
        });
        return app;
    }

    private static bool Cached(HttpContext context, string version, int seconds = 3600)
    {
        var etag = "\"" + version + "\"";
        context.Response.Headers.ETag = etag;
        context.Response.Headers.CacheControl = "public,max-age=" + seconds;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return context.Request.GetTypedHeaders().IfNoneMatch?.Any(tag => tag.Tag == "*" || tag.Tag == etag) == true;
    }
}
