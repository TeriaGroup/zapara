using Zapara.Server.Accounts;
using Zapara.Server.Notifications;

namespace Zapara.Server.Web;

internal static partial class WebEndpoints
{
    private static void MapNotifications(RouteGroupBuilder group)
    {
        Route(group, "GET", "/notifications/capabilities", c =>
        {
            var config = c.RequestServices.GetService<PushConfiguration>();
            return Task.FromResult(Json(new { available = config?.Available == true,
                publicKey = config?.PublicKey, reason = config?.Reason ?? "account_unavailable" }));
        }, authenticated: false);
        Route(group, "GET", "/notifications/subscriptions", async c => Json(await Push(c).ListAsync(Token(c), c.RequestAborted)));
        Route(group, "POST", "/notifications/subscriptions", async c =>
            Json(await Push(c).UpsertAsync(Token(c), await AccountBodyReader.Read<PushSubscriptionRequest>(c), c.RequestAborted), 201));
        Route(group, "DELETE", "/notifications/subscriptions/{subscriptionId}", async c =>
        {
            await AccountBodyReader.Empty(c);
            await Push(c).DeleteAsync(Token(c), Id(c, "subscriptionId"), c.RequestAborted);
            return Results.NoContent();
        });
        Route(group, "POST", "/notifications/test", async c =>
        {
            var request = await AccountBodyReader.Read<PushTestRequest>(c);
            var outcome = await Push(c).TestAsync(Token(c), request.SubscriptionId, c.RequestAborted);
            return Json(new { status = outcome == PushDeliveryOutcome.Accepted ? "accepted" : "unavailable" }, outcome == PushDeliveryOutcome.Accepted ? 202 : 503);
        });
    }
    private static PushSubscriptionService Push(HttpContext c)
    {
        return c.RequestServices.GetRequiredService<PushSubscriptionService>();
    }
}
