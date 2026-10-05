using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
namespace Zapara.Server.Web;
internal static partial class WebEndpoints
{
    private static void MapGroupSpace(RouteGroupBuilder group)
    {
        Route(group,"GET","/{communityId}/space",async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().SpaceAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),context.RequestAborted));
        });
        Route(group,"GET","/{communityId}/space/archive",async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ArchivedTopicsAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/categories",async context =>
        {
            CommunityHttpInput.Query(context);
            var body=await CommunityHttpInput.Body<GroupCategoryRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().SaveCategoryAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),body,context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/categories/{categoryId}/delete",async context =>
        {
            CommunityHttpInput.Query(context);
            await CommunityHttpInput.Empty(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().DeleteCategoryAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["categoryId"]),context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/topics/{topicId}/archive",async context =>
        {
            CommunityHttpInput.Query(context);
            var body=await CommunityHttpInput.Body<GroupArchiveRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().ArchiveTopicAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["topicId"]),body,context.RequestAborted));
        });
        Route(group,"GET","/{communityId}/space/topics/{topicId}/access",async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().TopicAccessAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["topicId"]),context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/topics/{topicId}/access",async context =>
        {
            CommunityHttpInput.Query(context);
            var body=await CommunityHttpInput.Body<GroupTopicAccessRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().SetTopicAccessAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["topicId"]),body,context.RequestAborted));
        });
        Route(group, "POST", "/{communityId}/space/topics/{topicId}/access-preview", async context =>
        {
            CommunityHttpInput.Query(context);
            var body = await CommunityHttpInput.Body<GroupTopicAccessRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().TopicAccessPreviewAsync(Token(context), CommunityHttpInput.Id(context.Request.RouteValues["communityId"]), CommunityHttpInput.Id(context.Request.RouteValues["topicId"]), body, context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/preview",async context =>
        {
            CommunityHttpInput.Query(context);
            var body=await CommunityHttpInput.Body<GroupPermissionPreviewRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().PreviewPermissionsAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),body,context.RequestAborted));
        });
        Route(group,"GET","/{communityId}/space/audit",async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().GroupAuditAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/roles/{roleId}",async context =>
        {
            CommunityHttpInput.Query(context);
            var body=await CommunityHttpInput.Body<GroupRoleSettingsRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().SaveRoleSettingsAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["roleId"]),body,context.RequestAborted));
        });
        Route(group,"GET","/{communityId}/space/roles/{roleId}/impact",async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().RoleImpactAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["roleId"]),context.RequestAborted));
        });
        Route(group,"GET","/{communityId}/space/topics/{topicId}/forms",async context =>
        {
            CommunityHttpInput.Query(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().FormsAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["topicId"]),context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/topics/{topicId}/forms",async context =>
        {
            CommunityHttpInput.Query(context);
            var body=await CommunityHttpInput.Body<GroupFormRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().CreateFormAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["topicId"]),body,context.RequestAborted));
        });
        Route(group,"POST","/{communityId}/space/forms/{formId}/response",async context =>
        {
            CommunityHttpInput.Query(context);
            var body=await CommunityHttpInput.Body<GroupFormAnswerRequest>(context);
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().SubmitFormAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["formId"]),body,context.RequestAborted));
        });
        Route(group,"GET","/{communityId}/space/forms/{formId}/responses",async context =>
        {
            CommunityHttpInput.Query(context,"after");
            return CommunityHttpResult.Json(await context.RequestServices.GetRequiredService<CommunityService>().FormResponsesAsync(Token(context),CommunityHttpInput.Id(context.Request.RouteValues["communityId"]),CommunityHttpInput.Id(context.Request.RouteValues["formId"]),context.RequestAborted,CommunityHttpInput.Cursor(context,"after")));
        });
    }
}
