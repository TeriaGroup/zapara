using Zapara.Server.Sync;
namespace Zapara.Server.Web;
internal static partial class WebEndpoints
{
    private static void MapSync(RouteGroupBuilder root, IConfiguration configuration)
    {
        if (!SyncConfiguration.IsEnabled(configuration)) return;
        var group = root.MapGroup("/sync");
        Route(group, "GET", "/metadata", async context =>
        {
            SyncHttpInput.Query(context);
            return SyncHttpResult.Json(await context.RequestServices.GetRequiredService<SyncService>().MetadataAsync(Token(context), context.RequestAborted));
        });
        Route(group, "POST", "/mutations", async context =>
        {
            SyncHttpInput.Query(context);
            var mutation = await SyncHttpInput.Mutation(context);
            var result = await context.RequestServices.GetRequiredService<SyncService>().MutateAsync(Token(context), mutation, context.RequestAborted);
            return SyncHttpResult.Json(result, result.Status);
        });
        Route(group, "GET", "/changes", async context =>
        {
            SyncHttpInput.Query(context, "epoch", "afterSequence", "limit");
            var epoch = SyncHttpInput.Id(context.Request.Query["epoch"]);
            var after = SyncHttpInput.After(context, "afterSequence");
            var limit = SyncHttpInput.Limit(context);
            return SyncHttpResult.Read(await context.RequestServices.GetRequiredService<SyncService>().ChangesAsync(Token(context), epoch, after, limit, context.RequestAborted));
        });
        Route(group, "POST", "/resync", async context =>
        {
            SyncHttpInput.Query(context);
            await SyncHttpInput.Empty(context);
            return SyncHttpResult.Json(await context.RequestServices.GetRequiredService<SyncService>().BeginResyncAsync(Token(context), context.RequestAborted));
        });
        Route(group, "GET", "/resync/{manifestId}", async context =>
        {
            SyncHttpInput.Query(context, "afterOrdinal", "limit");
            var id = SyncHttpInput.Id(context.Request.RouteValues["manifestId"] as string);
            var after = SyncHttpInput.After(context, "afterOrdinal");
            var limit = SyncHttpInput.Limit(context);
            return SyncHttpResult.Read(await context.RequestServices.GetRequiredService<SyncService>().ReadResyncPageAsync(Token(context), id, after, limit, context.RequestAborted));
        });
    }
}

