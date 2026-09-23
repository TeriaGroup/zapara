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
        MapVersion(app, 1);
        MapVersion(app, 2);
    }
    private static void MapVersion(WebApplication app, int version)
    {
        var group = app.MapGroup($"/api/v{version}/communities").RequireAuthorization("AccountUser").RequireRateLimiting("account-other");
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
        Route(group, "GET", "/{communityId}/join-request", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).GetOwnJoinRequestAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
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
        Route(group, "GET", "/{communityId}/desk", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).DeskAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/roles", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<GroupRoleNameRequest>(context);
            return CommunityHttpResult.Json(await Service(context).CreateRoleAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/{communityId}/roles/{roleId}", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<GroupRoleNameRequest>(context);
            return CommunityHttpResult.Json(await Service(context).RenameRoleAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["roleId"]), body, context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/roles/{roleId}/delete", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await Service(context).DeleteRoleAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["roleId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/roles/{roleId}/grants", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<GroupGrantRequest>(context);
            return CommunityHttpResult.Json(await Service(context).GrantRoleAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["roleId"]), body, context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/roles/{roleId}/grants/{userId}/delete", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await Service(context).RevokeRoleAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["roleId"]), CommunityHttpInput.Id(context.Request.RouteValues["userId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/members/{userId}/remove", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await Service(context).RemoveMemberAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["userId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/ballots", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).BallotsAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/ballots/headman", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<BallotDraftRequest>(context);
            return CommunityHttpResult.Json(await Service(context).OpenHeadmanBallotAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/{communityId}/ballots/collective", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<BallotDraftRequest>(context);
            return CommunityHttpResult.Json(await Service(context).ProposeBallotAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/{communityId}/ballots/{ballotId}/support", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await Service(context).SupportBallotAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["ballotId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/ballots/{ballotId}/votes", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<VoteRequest>(context);
            return CommunityHttpResult.Json(await Service(context).VoteBallotAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["ballotId"]), body, context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/ballots/{ballotId}/close", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await Service(context).CloseBallotAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["ballotId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/ballots/changes", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<BallotChangeRequest>(context);
            return CommunityHttpResult.Json(await Service(context).ProposeChangeAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/{communityId}/roles/{roleId}/powers", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<GroupPowerRequest>(context);
            return CommunityHttpResult.Json(await Service(context).SetRolePowerAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["roleId"]), body, context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/homework", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).ListHomeworkAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/homework/copies", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).ListHomeworkCopiesAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/homework/share", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<HomeworkUpsert>(context);
            return CommunityHttpResult.Json(await Service(context).ShareHomeworkAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
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
        Route(group, "GET", "/{communityId}/polls/{pollId}/vote", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).GetOwnVoteAsync(Bearer(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["pollId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/polls/{pollId}/results", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).ResultsAsync(Bearer(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["pollId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/home", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).GroupHomeAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/topics", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).TopicsAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/topics", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<GroupTopicRequest>(context);
            return CommunityHttpResult.Json(await Service(context).CreateTopicAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/{communityId}/topics/{topicId}", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<GroupTopicRequest>(context);
            return CommunityHttpResult.Json(await Service(context).RenameTopicAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["topicId"]), body, context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/topics/{topicId}/delete", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await Service(context).DeleteTopicAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["topicId"]), context.RequestAborted));
        });
        Route(group, "POST", "/direct", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<OpenDirectRequest>(context);
            return CommunityHttpResult.Json(await Service(context).OpenDirectAsync(Bearer(context), body, context.RequestAborted), 201);
        });
        Route(group, "GET", "/conversations/{conversationId}/messages", async context =>
        {
            CommunityHttpInput.Query(context, "before", "after", "topic");
            var before = CommunityHttpInput.Cursor(context, "before");
            var after = CommunityHttpInput.Cursor(context, "after");
            if (before is not null && after is not null) throw new CommunityInputException();
            var topic = context.Request.Query.TryGetValue("topic", out var topicRaw) ? topicRaw.ToString() : null;
            return CommunityHttpResult.Json(await Service(context).ListMessagesAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), before, after, topic, context.RequestAborted));
        });
        Route(group, "POST", "/conversations/{conversationId}/media", context => CommunityMedia.Post(context, Bearer(context)));
        Route(group, "GET", "/conversations/{conversationId}/messages/{messageId}/media", context => CommunityMedia.Get(context, Bearer(context)));
        Route(group, "POST", "/conversations/{conversationId}/messages", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<SendMessageRequest>(context);
            return CommunityHttpResult.Json(await Service(context).SendMessageAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/conversations/{conversationId}/messages/{messageId}/edit", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<SendMessageRequest>(context);
            return CommunityHttpResult.Json(await Service(context).EditMessageAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), CommunityHttpInput.Id(context.Request.RouteValues["messageId"]), body, context.RequestAborted));
        });
        Route(group, "POST", "/conversations/{conversationId}/messages/{messageId}/delete", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await Service(context).DeleteMessageAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), CommunityHttpInput.Id(context.Request.RouteValues["messageId"]), context.RequestAborted));
        });
        Route(group, "POST", "/conversations/{conversationId}/messages/{messageId}/react", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<ReactMessageRequest>(context);
            return CommunityHttpResult.Json(await Service(context).ReactMessageAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), CommunityHttpInput.Id(context.Request.RouteValues["messageId"]), body, context.RequestAborted));
        });
        Route(group, "POST", "/conversations/{conversationId}/topic-messages", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<TopicMessageRequest>(context);
            return CommunityHttpResult.Json(await Service(context).SendTopicMessageAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/conversations/{conversationId}/read", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await Service(context).MarkReadAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), context.RequestAborted));
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
