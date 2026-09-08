using Zapara.Contracts.Sync;
using Zapara.Server.Accounts;

namespace Zapara.Server.Sync;

internal static class SyncEndpoints
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/sync").WithMetadata(new AccountEndpoint())
            .RequireAuthorization("AccountUser").RequireRateLimiting("account-other");
        Route(group, "GET", "/metadata", async context =>
        {
            SyncHttpInput.Query(context);
            return SyncHttpResult.Json(await Service(context).MetadataAsync(Bearer(context), context.RequestAborted));
        });
        Route(group, "POST", "/mutations", async context =>
        {
            SyncHttpInput.Query(context);
            var mutation = await SyncHttpInput.Mutation(context);
            var result = await Service(context).MutateAsync(Bearer(context), mutation, context.RequestAborted);
            return SyncHttpResult.Json(result, result.Status);
        });
        Route(group, "GET", "/changes", async context =>
        {
            SyncHttpInput.Query(context, "epoch", "afterSequence", "limit");
            var epoch = SyncHttpInput.Id(context.Request.Query["epoch"]);
            var after = SyncHttpInput.After(context, "afterSequence");
            var limit = SyncHttpInput.Limit(context);
            return SyncHttpResult.Read(await Service(context).ChangesAsync(Bearer(context), epoch, after, limit, context.RequestAborted));
        });
        Route(group, "POST", "/resync", async context =>
        {
            SyncHttpInput.Query(context);
            await SyncHttpInput.Empty(context);
            return SyncHttpResult.Json(await Service(context).BeginResyncAsync(Bearer(context), context.RequestAborted));
        });
        Route(group, "GET", "/resync/{manifestId}", async context =>
        {
            SyncHttpInput.Query(context, "afterOrdinal", "limit");
            var id = SyncHttpInput.Id(context.Request.RouteValues["manifestId"] as string);
            var after = SyncHttpInput.After(context, "afterOrdinal");
            var limit = SyncHttpInput.Limit(context);
            return SyncHttpResult.Read(await Service(context).ReadResyncPageAsync(Bearer(context), id, after, limit, context.RequestAborted));
        });
    }
    private static void Route(RouteGroupBuilder group, string method, string path, Func<HttpContext, Task<IResult>> handler)
        => group.MapMethods(path, new[] { method }, (Delegate)(Func<HttpContext, Task<IResult>>)(async context =>
        {
            try { return await handler(context); }
            catch (SyncInputException exception)
            { return SyncHttpResult.Error(new SyncError(exception.Status, exception.Status == 413 ? "payload_too_large" : "invalid_request")); }
            catch (AccountServiceException exception)
            {
                if (exception.Failure == AccountFailure.RateLimited) context.Response.Headers.RetryAfter = "60";
                return AccountErrors.From(exception);
            }
        }));
    private static SyncService Service(HttpContext context) => context.RequestServices.GetRequiredService<SyncService>();
    private static string Bearer(HttpContext context) => OpaqueAccountHandler.Bearer(context.Request);
}
