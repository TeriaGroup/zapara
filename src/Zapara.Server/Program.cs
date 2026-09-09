using Npgsql;
using Zapara.Server.Timetable;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;
using Zapara.Server.Communities;
using Zapara.Server.Admin;
using Zapara.Server.Platform;

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
            return new SnapshotStore(provider.GetRequiredService<NpgsqlDataSource>(), configuration.Schema,
                provider.GetRequiredService<TimeProvider>(), configuration.CreateDedicatedConnection);
        });
        builder.Services.AddAccounts(builder.Configuration);
        builder.Services.AddExternalAuth(builder.Configuration);
        builder.Services.AddSync(builder.Configuration);
        builder.Services.AddCommunities(builder.Configuration);
        builder.Services.AddAdmin(builder.Configuration);
        builder.Services.AddSingleton(CreatePlatformReady);
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
        app.MapGet("/health/platform", (Delegate)((HttpContext context) => PlatformAsync(context)));
        app.Run();
    }

    private static PlatformReady CreatePlatformReady(IServiceProvider services) =>
        PlatformReady.FromConfiguration(
            services.GetRequiredService<IConfiguration>(),
            ct => ProbeTimetableAsync(services, ct),
            ct => ProbeNamespaceAsync(services, ct, sp => sp.GetRequiredService<AccountsConfiguration>().Schema),
            ct => ProbeNamespaceAsync(services, ct, sp => sp.GetRequiredService<SyncConfiguration>().Schema),
            ct => ProbeNamespaceAsync(services, ct, sp => sp.GetRequiredService<CommunitiesConfiguration>().Schema),
            ct => ProbeNamespaceAsync(services, ct, sp => sp.GetRequiredService<AdminConfiguration>().Schema));

    private static async Task<IResult> PlatformAsync(HttpContext context) =>
        PlatformReady.ToResult(await context.RequestServices.GetRequiredService<PlatformReady>()
            .CheckAsync(context.RequestAborted));

    private static async Task<bool> ProbeTimetableAsync(IServiceProvider services, CancellationToken ct)
    {
        try
        {
            _ = await services.GetRequiredService<SnapshotStore>().ReadSelectionAsync(null, ct);
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<bool> ProbeNamespaceAsync(IServiceProvider services, CancellationToken ct,
        Func<IServiceProvider, string> schema)
    {
        try
        {
            var name = schema(services);
            await using var connection = services.GetRequiredService<AccountsDataSource>().CreateConnection();
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS(SELECT FROM pg_namespace WHERE nspname=@schema)", connection);
            command.Parameters.AddWithValue("schema", name);
            return await command.ExecuteScalarAsync(ct) is true;
        }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
