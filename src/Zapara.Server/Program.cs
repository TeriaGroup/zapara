using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;
using Zapara.Server.Timetable;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;
using Zapara.Server.Communities;
using Zapara.Server.Social;
using Zapara.Server.Admin;
using Zapara.Server.Platform;
using Zapara.Server.Web;

namespace Zapara.Server;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseStaticWebAssets();
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
        builder.Services.AddSingleton<Zapara.Server.Storage.RoutingObjectStore>();
        builder.Services.AddSingleton<Zapara.Server.Social.IObjectStore>(provider => provider.GetRequiredService<Zapara.Server.Storage.RoutingObjectStore>());
        builder.Services.AddSingleton<Zapara.Server.Accounts.IContentArchive>(provider => provider.GetRequiredService<Zapara.Server.Storage.RoutingObjectStore>());
        builder.Services.AddSocial(builder.Configuration);
        if (Zapara.Server.Accounts.AccountsConfiguration.IsEnabled(builder.Configuration))
            builder.Services.AddSingleton<Zapara.Server.Operator.SupportStore>();
        builder.Services.AddAdmin(builder.Configuration);
        builder.Services.AddWebClient(builder.Configuration);
        builder.Services.AddPublicCatalogs();
        builder.Services.AddTimetableRefresh(builder.Configuration);
        builder.Services.AddSingleton(CreatePlatformReady);
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            // TLS ends at the reverse proxy. Kestrel is not published off the Docker network.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });
        var app = builder.Build();
        app.UseForwardedHeaders();
        app.UseExceptionHandler(handler => handler.Run(context =>
        {
            var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
            context.Response.Headers.CacheControl = "no-store";
            // Never log the message: it can carry query secrets, passwords or provider tokens.
            IResult? clientError = null;
            try { clientError = MapClientError(error); }
            catch (Exception) { clientError = null; }
            if (context.Response.HasStarted || clientError is null)
            {
                if (!context.Response.HasStarted)
                    app.Logger.LogError("Ошибка HTTP: internal_error");
                return context.Response.HasStarted ? Task.CompletedTask : ApiErrors.InternalError().ExecuteAsync(context);
            }
            app.Logger.LogError("Ошибка HTTP: client_error");
            return clientError.ExecuteAsync(context);
        }));
        app.MapAccounts();
        app.MapExternalAuth();
        app.MapSync();
        app.MapCommunities();
        SocialHttp.MapNative(app);
        app.MapAdmin();
        app.MapWebClient();
        Zapara.Server.Operator.SupportEndpoints.MapSupport(app);
        app.MapPublicCatalogs();
        app.MapTimetableEndpoints();
        app.MapWebShell();
        app.MapGet("/health/platform", (Delegate)((HttpContext context) => PlatformAsync(context)));
        app.Run();
    }

    private static IResult? MapClientError(Exception? error) => error switch
    {
        Microsoft.AspNetCore.Http.BadHttpRequestException bad when bad.StatusCode is >= 400 and < 500
            => ApiErrors.ForStatus(bad.StatusCode, bad.StatusCode == 413 ? "payload_too_large" : "invalid_request"),
        AdminException admin => ApiErrors.ForStatus(admin.Status, admin.Code),
        AccountBodyException body => ApiErrors.ForStatus(body.Status, body.Status == 413 ? "payload_too_large" : "invalid_request"),
        AccountServiceException account => AccountErrors.From(account),
        ExternalAuthException external => AccountErrors.Problem(external.Status, external.Code),
        WebRequestException web => AccountErrors.Problem(web.Status, web.Code),
        Zapara.Server.Notifications.PushOperationException push => AccountErrors.Problem(push.Status, push.Code),
        SyncInputException sync when sync.Status is 400 or 413
            => SyncHttpResult.Error(new Zapara.Contracts.Sync.SyncError(sync.Status, sync.Status == 413 ? "payload_too_large" : "invalid_request")),
        Zapara.Server.Communities.CommunityInputException input
            => Zapara.Server.Communities.CommunityHttpResult.Problem(input.Status, input.Status == 413 ? "payload_too_large" : "invalid_request"),
        Zapara.Server.Communities.CommunityServiceException community
            => Zapara.Server.Communities.CommunityHttpResult.From(community),
        Zapara.Server.Social.SocialException social => SocialHttp.Problem(social),
        _ => null
    };

    private static PlatformReady CreatePlatformReady(IServiceProvider services) =>
        PlatformReady.FromConfiguration(
            services.GetRequiredService<IConfiguration>(),
            ct => ProbeTimetableAsync(services, ct),
            ct => ProbeNamespaceAsync(services, ct, sp => sp.GetRequiredService<AccountsConfiguration>().Schema,
                (sp, connection, tx, token) => AccountsMigrations.VerifyCurrentPreparedSchemaAsync(connection, tx, sp.GetRequiredService<AccountsConfiguration>().Schema, token)),
            ct => ProbeNamespaceAsync(services, ct, sp => sp.GetRequiredService<SyncConfiguration>().Schema,
                (sp, connection, tx, token) => new SyncMigrations(sp.GetRequiredService<AccountsDataSource>(), sp.GetRequiredService<SyncConfiguration>()).VerifyCurrentPreparedSchemaAsync(connection, tx, token)),
            ct => ProbeNamespaceAsync(services, ct, sp => sp.GetRequiredService<CommunitiesConfiguration>().Schema,
                (sp, connection, tx, token) => new CommunitiesMigrations(sp.GetRequiredService<AccountsDataSource>(), sp.GetRequiredService<CommunitiesConfiguration>()).VerifyCurrentPreparedSchemaAsync(connection, tx, token)),
            ct => ProbeNamespaceAsync(services, ct, sp => sp.GetRequiredService<AdminConfiguration>().Schema,
                (sp, connection, tx, token) => new AdminMigrations(sp.GetRequiredService<AccountsDataSource>(), sp.GetRequiredService<AdminConfiguration>()).VerifyCurrentPreparedSchemaAsync(connection, tx, token)));

    private static async Task<IResult> PlatformAsync(HttpContext context) =>
        PlatformReady.ToResult(await context.RequestServices.GetRequiredService<PlatformReady>()
            .CheckAsync(context.RequestAborted));

    private static Task<bool> ProbeTimetableAsync(IServiceProvider services, CancellationToken ct) =>
        ProbeSafelyAsync(token => services.GetRequiredService<SnapshotStore>().IsReadyAsync(token), ct);

    private static Task<bool> ProbeNamespaceAsync(IServiceProvider services, CancellationToken ct,
        Func<IServiceProvider, string> schema, Func<IServiceProvider, NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> verify) =>
        ProbeSafelyAsync(token => PreparedSchemaProbe.ReadAsync(services.GetRequiredService<AccountsDataSource>(), schema(services),
            (connection, transaction, readToken) => verify(services, connection, transaction, readToken), token), ct);

    private static async Task<bool> ProbeSafelyAsync(Func<CancellationToken, Task<bool>> probe, CancellationToken ct)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            return await probe(bounded.Token);
        }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
