using Zapara.Server.Timetable;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;
using Zapara.Server.Communities;
using Zapara.Server.Admin;

namespace Zapara.Server;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (string.IsNullOrEmpty(builder.Configuration["urls"]))
            builder.WebHost.UseUrls("http://127.0.0.1:5187");
        // Default request/exception diagnostics can include query strings and raw exceptions.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.None);
        builder.Logging.AddFilter("Npgsql", LogLevel.None);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(provider => TimetableConfiguration.FromConfiguration(
            provider.GetRequiredService<IConfiguration>()));
        builder.Services.AddSingleton(provider => provider.GetRequiredService<TimetableConfiguration>().CreateDataSource());
        builder.Services.AddSingleton(provider =>
        {
            var configuration = provider.GetRequiredService<TimetableConfiguration>();
            return new SnapshotStore(provider.GetRequiredService<Npgsql.NpgsqlDataSource>(), configuration.Schema,
                provider.GetRequiredService<TimeProvider>(), configuration.CreateDedicatedConnection);
        });
        builder.Services.AddAccounts(builder.Configuration);
        builder.Services.AddExternalAuth(builder.Configuration);
        builder.Services.AddSync(builder.Configuration);
        builder.Services.AddCommunities(builder.Configuration);
        builder.Services.AddAdmin(builder.Configuration);
        var app = builder.Build();
        app.UseExceptionHandler(handler => handler.Run(context =>
        {
            context.Response.Headers.CacheControl = "no-store";
            app.Logger.LogError("Ошибка HTTP: internal_error");
            return ApiErrors.InternalError().ExecuteAsync(context);
        }));
        app.MapAccounts();
        app.MapExternalAuth();
        app.MapSync();
        app.MapCommunities();
        app.MapAdmin();
        app.MapTimetableEndpoints();
        app.Run();
    }
}
