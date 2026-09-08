using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Communities;
using Zapara.Server.Accounts;

namespace Zapara.Server.Communities;

internal static class CommunityEndpoints
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/communities").RequireAuthorization("AccountUser").RequireRateLimiting("account-other");
        Route(group, "GET", "", async context =>
        {
            CommunityHttpInput.Query(context, "groupId");
            return CommunityHttpResult.Json(await Service(context).ListAsync(Bearer(context), CommunityHttpInput.GroupId(context), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).GetAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/join-requests", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await Service(context).RequestJoinAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted), 201);
        });
        Route(group, "GET", "/{communityId}/join-requests", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).ListJoinRequestsAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/join-requests/{requestId}/accept", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await Service(context).AcceptJoinAsync(Bearer(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["requestId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/join-requests/{requestId}/reject", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await Service(context).RejectJoinAsync(Bearer(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["requestId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/members", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).ListMembersAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/staff", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).ListStaffAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/homework", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).ListHomeworkAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/homework", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<HomeworkUpsert>(context);
            return CommunityHttpResult.Json(await Service(context).PublishHomeworkAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "GET", "/{communityId}/homework/{homeworkId}", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).GetHomeworkAsync(Bearer(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["homeworkId"]), context.RequestAborted));
        });
        Route(group, "PUT", "/{communityId}/homework/{homeworkId}", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<HomeworkUpsert>(context);
            return CommunityHttpResult.Json(await Service(context).UpdateHomeworkAsync(Bearer(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["homeworkId"]), body, context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/homework/{homeworkId}/completion", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).GetCompletionAsync(Bearer(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["homeworkId"]), context.RequestAborted));
        });
        Route(group, "PUT", "/{communityId}/homework/{homeworkId}/completion", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<CompletionUpsert>(context);
            return CommunityHttpResult.Json(await Service(context).UpsertCompletionAsync(Bearer(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["homeworkId"]), body, context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/announcements", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).ListAnnouncementsAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/announcements", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<AnnouncementUpsert>(context);
            return CommunityHttpResult.Json(await Service(context).PublishAnnouncementAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "PUT", "/{communityId}/announcements/{announcementId}", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<AnnouncementUpsert>(context);
            return CommunityHttpResult.Json(await Service(context).UpdateAnnouncementAsync(Bearer(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["announcementId"]), body, context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/polls", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).ListPollsAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/polls", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<PollUpsert>(context);
            return CommunityHttpResult.Json(await Service(context).PublishPollAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "GET", "/{communityId}/polls/{pollId}", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).GetPollAsync(Bearer(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["pollId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/polls/{pollId}/votes", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<VoteRequest>(context);
            return CommunityHttpResult.Json(await Service(context).VoteAsync(Bearer(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["pollId"]), body, context.RequestAborted), 201);
        });
        Route(group, "GET", "/{communityId}/polls/{pollId}/results", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).ResultsAsync(Bearer(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["pollId"]), context.RequestAborted));
        });
    }
    private static void Route(RouteGroupBuilder group, string method, string path, Func<HttpContext, Task<IResult>> handler)
        => group.MapMethods(path, new[] { method }, (Delegate)(Func<HttpContext, Task<IResult>>)(async context =>
        {
            try { return await handler(context); }
            catch (CommunityInputException exception)
            { return CommunityHttpResult.Problem(exception.Status, exception.Status == 413 ? "payload_too_large" : "invalid_request"); }
            catch (CommunityServiceException exception) { return CommunityHttpResult.From(exception); }
            catch (AccountServiceException exception)
            {
                if (exception.Failure == AccountFailure.RateLimited) context.Response.Headers.RetryAfter = "60";
                return CommunityHttpResult.From(exception);
            }
        }));
    private static CommunityService Service(HttpContext context) => context.RequestServices.GetRequiredService<CommunityService>();
    private static string Bearer(HttpContext context)
    {
        var values = context.Request.Headers.Authorization;
        if (values.Count != 1 || values[0] is not { Length: 53 } header ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new AccountServiceException(AccountFailure.InvalidSession);
        try { return AccountValidation.Token(header[7..], "za_"); }
        catch (ArgumentException) { throw new AccountServiceException(AccountFailure.InvalidSession); }
    }
}
