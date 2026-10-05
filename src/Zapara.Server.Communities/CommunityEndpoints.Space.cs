using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
namespace Zapara.Server.Communities;
internal static partial class CommunityEndpoints
{
    private static void MapGroupSpace(RouteGroupBuilder group)
    {
        Route(group,"GET","/{communityId}/space",async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).SpaceAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),context.RequestAborted));
        });
        Route(group,"GET","/{communityId}/space/archive",async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).ArchivedTopicsAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/categories",async context =>
        {
            CommunityHttpInput.Query(context);
            var body=await CommunityHttpInput.Body<GroupCategoryRequest>(context);
            return CommunityHttpResult.Json(await Service(context).SaveCategoryAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),body,context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/categories/{categoryId}/delete",async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await Service(context).DeleteCategoryAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["categoryId"]),context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/topics/{topicId}/archive",async context =>
        {
            CommunityHttpInput.Query(context);
            var body=await CommunityHttpInput.Body<GroupArchiveRequest>(context);
            return CommunityHttpResult.Json(await Service(context).ArchiveTopicAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["topicId"]),body,context.RequestAborted));
        });
        Route(group,"GET","/{communityId}/space/topics/{topicId}/access",async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).TopicAccessAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["topicId"]),context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/topics/{topicId}/access",async context =>
        {
            CommunityHttpInput.Query(context);
            var body=await CommunityHttpInput.Body<GroupTopicAccessRequest>(context);
            return CommunityHttpResult.Json(await Service(context).SetTopicAccessAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["topicId"]),body,context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/space/topics/{topicId}/access-preview", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<GroupTopicAccessRequest>(context);
            return CommunityHttpResult.Json(await Service(context).TopicAccessPreviewAsync(Bearer(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["topicId"]), body, context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/preview",async context =>
        {
            CommunityHttpInput.Query(context);
            var body=await CommunityHttpInput.Body<GroupPermissionPreviewRequest>(context);
            return CommunityHttpResult.Json(await Service(context).PreviewPermissionsAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),body,context.RequestAborted));
        });
        Route(group,"GET","/{communityId}/space/audit",async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).GroupAuditAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/roles/{roleId}",async context =>
        {
            CommunityHttpInput.Query(context);
            var body=await CommunityHttpInput.Body<GroupRoleSettingsRequest>(context);
            return CommunityHttpResult.Json(await Service(context).SaveRoleSettingsAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["roleId"]),body,context.RequestAborted));
        });
        Route(group,"GET","/{communityId}/space/roles/{roleId}/impact",async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).RoleImpactAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["roleId"]),context.RequestAborted));
        });
        Route(group,"GET","/{communityId}/space/topics/{topicId}/forms",async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await Service(context).FormsAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["topicId"]),context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/topics/{topicId}/forms",async context =>
        {
            CommunityHttpInput.Query(context);
            var body=await CommunityHttpInput.Body<GroupFormRequest>(context);
            return CommunityHttpResult.Json(await Service(context).CreateFormAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["topicId"]),body,context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/forms/{formId}/response",async context =>
        {
            CommunityHttpInput.Query(context);
            var body=await CommunityHttpInput.Body<GroupFormAnswerRequest>(context);
            return CommunityHttpResult.Json(await Service(context).SubmitFormAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["formId"]),body,context.RequestAborted));
        });
        Route(group,"GET","/{communityId}/space/forms/{formId}/responses",async context =>
        {
            CommunityHttpInput.Query(context,"after");
            return CommunityHttpResult.Json(await Service(context).FormResponsesAsync(Bearer(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["formId"]),context.RequestAborted,CommunityHttpInput.Cursor(context,"after")));
        });
    }
}
