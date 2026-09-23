using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
namespace Zapara.Server.Web;
internal static partial class WebEndpoints
{
    private static void MapCommunities(RouteGroupBuilder root, IConfiguration configuration)
    {
        if (!CommunitiesConfiguration.IsEnabled(configuration)) return;
        var group = root.MapGroup("/communities");
        Route(group, "GET", "", async context =>
        {
            CommunityHttpInput.Query(context, "groupId");
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ListAsync(Token(context), CommunityHttpInput.GroupId(context), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().GetAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/join-requests", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().RequestJoinAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted), 201);
        });
        Route(group, "GET", "/{communityId}/join-request", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().GetOwnJoinRequestAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/join-requests", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ListJoinRequestsAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/join-requests/{requestId}/accept", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().AcceptJoinAsync(Token(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["requestId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/join-requests/{requestId}/reject", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().RejectJoinAsync(Token(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["requestId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/members", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ListMembersAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/staff", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ListStaffAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/desk", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().DeskAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/roles", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<GroupRoleNameRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().CreateRoleAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/{communityId}/roles/{roleId}", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<GroupRoleNameRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().RenameRoleAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["roleId"]), body, context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/roles/{roleId}/delete", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().DeleteRoleAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["roleId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/roles/{roleId}/grants", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<GroupGrantRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().GrantRoleAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["roleId"]), body, context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/roles/{roleId}/grants/{userId}/delete", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().RevokeRoleAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["roleId"]), CommunityHttpInput.Id(context.Request.RouteValues["userId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/members/{userId}/remove", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().RemoveMemberAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["userId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/ballots", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().BallotsAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/ballots/headman", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<BallotDraftRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().OpenHeadmanBallotAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/{communityId}/ballots/collective", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<BallotDraftRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ProposeBallotAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/{communityId}/ballots/{ballotId}/support", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().SupportBallotAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["ballotId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/ballots/{ballotId}/votes", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<VoteRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().VoteBallotAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["ballotId"]), body, context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/ballots/{ballotId}/close", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().CloseBallotAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["ballotId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/ballots/changes", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<BallotChangeRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ProposeChangeAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/{communityId}/roles/{roleId}/powers", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<GroupPowerRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().SetRolePowerAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["roleId"]), body, context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/homework", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ListHomeworkAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/homework/copies", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ListHomeworkCopiesAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/homework/share", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<HomeworkUpsert>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ShareHomeworkAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/{communityId}/homework", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<HomeworkUpsert>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().PublishHomeworkAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "GET", "/{communityId}/homework/{homeworkId}", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().GetHomeworkAsync(Token(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["homeworkId"]), context.RequestAborted));
        });
        Route(group, "PUT", "/{communityId}/homework/{homeworkId}", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<HomeworkUpsert>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().UpdateHomeworkAsync(Token(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["homeworkId"]), body, context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/homework/{homeworkId}/completion", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().GetCompletionAsync(Token(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["homeworkId"]), context.RequestAborted));
        });
        Route(group, "PUT", "/{communityId}/homework/{homeworkId}/completion", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<CompletionUpsert>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().UpsertCompletionAsync(Token(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["homeworkId"]), body, context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/announcements", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ListAnnouncementsAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/announcements", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<AnnouncementUpsert>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().PublishAnnouncementAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "PUT", "/{communityId}/announcements/{announcementId}", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<AnnouncementUpsert>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().UpdateAnnouncementAsync(Token(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["announcementId"]), body, context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/polls", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ListPollsAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/polls", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<PollUpsert>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().PublishPollAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "GET", "/{communityId}/polls/{pollId}", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().GetPollAsync(Token(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["pollId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/polls/{pollId}/votes", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<VoteRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().VoteAsync(Token(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["pollId"]), body, context.RequestAborted), 201);
        });
        Route(group, "GET", "/{communityId}/polls/{pollId}/vote", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().GetOwnVoteAsync(Token(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["pollId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/polls/{pollId}/results", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ResultsAsync(Token(context),
                CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),
                CommunityHttpInput.Id(context.Request.RouteValues["pollId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/home", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().GroupHomeAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "GET", "/{communityId}/topics", async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().TopicsAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/topics", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<GroupTopicRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().CreateTopicAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/{communityId}/topics/{topicId}", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<GroupTopicRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().RenameTopicAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["topicId"]), body, context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/topics/{topicId}/delete", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().DeleteTopicAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["topicId"]), context.RequestAborted));
        });
        Route(group, "POST", "/direct", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<OpenDirectRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().OpenDirectAsync(Token(context), body, context.RequestAborted), 201);
        });
        Route(group, "GET", "/conversations/{conversationId}/messages", async context =>
        {
            CommunityHttpInput.Query(context, "before", "after", "topic");
            var before = CommunityHttpInput.Cursor(context, "before");
            var after = CommunityHttpInput.Cursor(context, "after");
            if (before is not null && after is not null) throw new CommunityInputException();
            var topic = context.Request.Query.TryGetValue("topic", out var topicRaw) ? topicRaw.ToString() : null;
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ListMessagesAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), before, after, topic, context.RequestAborted));
        });
        Route(group, "POST", "/conversations/{conversationId}/media", context => CommunityMedia.Post(context, Token(context)));
        Route(group, "GET", "/conversations/{conversationId}/messages/{messageId}/media", context => CommunityMedia.Get(context, Token(context)));
        Route(group, "POST", "/conversations/{conversationId}/messages", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<SendMessageRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().SendMessageAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/conversations/{conversationId}/messages/{messageId}/edit", async context =>
        {
            CommunityHttpInput.Query(context);
            var edit = await CommunityHttpInput.Body<SendMessageRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().EditMessageAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), CommunityHttpInput.Id(context.Request.RouteValues["messageId"]), edit, context.RequestAborted));
        });
        Route(group, "POST", "/conversations/{conversationId}/messages/{messageId}/delete", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().DeleteMessageAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), CommunityHttpInput.Id(context.Request.RouteValues["messageId"]), context.RequestAborted));
        });
        Route(group, "POST", "/conversations/{conversationId}/messages/{messageId}/react", async context =>
        {
            CommunityHttpInput.Query(context);
            var react = await CommunityHttpInput.Body<ReactMessageRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ReactMessageAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), CommunityHttpInput.Id(context.Request.RouteValues["messageId"]), react, context.RequestAborted));
        });
        Route(group, "POST", "/conversations/{conversationId}/topic-messages", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<TopicMessageRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().SendTopicMessageAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), body, context.RequestAborted), 201);
        });
        Route(group, "POST", "/conversations/{conversationId}/read", async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().MarkReadAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["conversationId"]), context.RequestAborted));
        });
    }
}
