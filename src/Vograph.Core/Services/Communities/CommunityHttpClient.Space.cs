using Zapara.Contracts.Communities;
namespace Vograph.Core.Services.Communities;

public sealed partial class CommunityHttpClient
{
    public Task<GroupTopicAccessPreviewResponse> TopicAccessPreviewAsync(string accessToken, Guid communityId, Guid topicId, GroupTopicAccessRequest request, CancellationToken ct = default)
        => SendAsync<GroupTopicAccessPreviewResponse>(HttpMethod.Post, "/" + Id(communityId) + "/space/topics/" + Id(topicId) + "/access-preview", Required(request), Access(accessToken), 200, ct);
    public Task<GroupSpaceResponse> SpaceAsync(string accessToken, Guid communityId, CancellationToken ct = default)
        => SendAsync<GroupSpaceResponse>(HttpMethod.Get, "/" + Id(communityId) + "/space", null, Access(accessToken), 200, ct);
    public Task<GroupTopicListResponse> ArchivedTopicsAsync(string accessToken, Guid communityId, CancellationToken ct = default)
        => SendAsync<GroupTopicListResponse>(HttpMethod.Get, "/" + Id(communityId) + "/space/archive", null, Access(accessToken), 200, ct);
    public Task<GroupSpaceResponse> SaveCategoryAsync(string accessToken, Guid communityId, GroupCategoryRequest request, CancellationToken ct = default)
        => SendAsync<GroupSpaceResponse>(HttpMethod.Post, "/" + Id(communityId) + "/space/categories", Required(request), Access(accessToken), 200, ct);
    public Task<GroupSpaceResponse> DeleteCategoryAsync(string accessToken, Guid communityId, Guid categoryId, CancellationToken ct = default)
        => SendAsync<GroupSpaceResponse>(HttpMethod.Post, "/" + Id(communityId) + "/space/categories/" + Id(categoryId) + "/delete", null, Access(accessToken), 200, ct);
    public Task<GroupSpaceResponse> ArchiveTopicAsync(string accessToken, Guid communityId, Guid topicId, GroupArchiveRequest request, CancellationToken ct = default)
        => SendAsync<GroupSpaceResponse>(HttpMethod.Post, "/" + Id(communityId) + "/space/topics/" + Id(topicId) + "/archive", Required(request), Access(accessToken), 200, ct);
    public Task<GroupTopicAccessResponse> TopicAccessAsync(string accessToken, Guid communityId, Guid topicId, CancellationToken ct = default)
        => SendAsync<GroupTopicAccessResponse>(HttpMethod.Get, "/" + Id(communityId) + "/space/topics/" + Id(topicId) + "/access", null, Access(accessToken), 200, ct);
    public Task<GroupSpaceResponse> SetTopicAccessAsync(string accessToken, Guid communityId, Guid topicId, GroupTopicAccessRequest request, CancellationToken ct = default)
        => SendAsync<GroupSpaceResponse>(HttpMethod.Post, "/" + Id(communityId) + "/space/topics/" + Id(topicId) + "/access", Required(request), Access(accessToken), 200, ct);
    public Task<GroupPermissionPreviewResponse> PreviewPermissionsAsync(string accessToken, Guid communityId, GroupPermissionPreviewRequest request, CancellationToken ct = default)
        => SendAsync<GroupPermissionPreviewResponse>(HttpMethod.Post, "/" + Id(communityId) + "/space/preview", Required(request), Access(accessToken), 200, ct);
    public Task<GroupAuditResponse> GroupAuditAsync(string accessToken, Guid communityId, CancellationToken ct = default)
        => SendAsync<GroupAuditResponse>(HttpMethod.Get, "/" + Id(communityId) + "/space/audit", null, Access(accessToken), 200, ct);
    public Task<GroupDeskResponse> SaveRoleSettingsAsync(string accessToken, Guid communityId, Guid roleId, GroupRoleSettingsRequest request, CancellationToken ct = default)
        => SendAsync<GroupDeskResponse>(HttpMethod.Post, "/" + Id(communityId) + "/space/roles/" + Id(roleId) + "", Required(request), Access(accessToken), 200, ct);
    public Task<GroupRoleImpactResponse> RoleImpactAsync(string accessToken, Guid communityId, Guid roleId, CancellationToken ct = default)
        => SendAsync<GroupRoleImpactResponse>(HttpMethod.Get, "/" + Id(communityId) + "/space/roles/" + Id(roleId) + "/impact", null, Access(accessToken), 200, ct);
    public Task<GroupFormListResponse> FormsAsync(string accessToken, Guid communityId, Guid topicId, CancellationToken ct = default)
        => SendAsync<GroupFormListResponse>(HttpMethod.Get, "/" + Id(communityId) + "/space/topics/" + Id(topicId) + "/forms", null, Access(accessToken), 200, ct);
    public Task<GroupFormListResponse> CreateFormAsync(string accessToken, Guid communityId, Guid topicId, GroupFormRequest request, CancellationToken ct = default)
        => SendAsync<GroupFormListResponse>(HttpMethod.Post, "/" + Id(communityId) + "/space/topics/" + Id(topicId) + "/forms", Required(request), Access(accessToken), 200, ct);
    public Task<GroupFormResponse> SubmitFormAsync(string accessToken, Guid communityId, Guid formId, GroupFormAnswerRequest request, CancellationToken ct = default)
        => SendAsync<GroupFormResponse>(HttpMethod.Post, "/" + Id(communityId) + "/space/forms/" + Id(formId) + "/response", Required(request), Access(accessToken), 200, ct);
    public Task<GroupFormResponsesResponse> FormResponsesAsync(string accessToken, Guid communityId, Guid formId, CancellationToken ct = default, Guid? after = null)
        => SendAsync<GroupFormResponsesResponse>(HttpMethod.Get, "/" + Id(communityId) + "/space/forms/" + Id(formId) + "/responses" + (after is Guid cursor ? "?after=" + Id(cursor) : ""), null, Access(accessToken), 200, ct);
    public Task<IReadOnlyList<GroupHomeworkCopyResponse>> ListHomeworkCopiesAsync(string accessToken, Guid communityId, CancellationToken ct = default, Guid? topicId = null)
        => SendList<GroupHomeworkCopyResponse>(HttpMethod.Get, "/" + Id(communityId) + "/homework/copies" + (topicId is Guid id ? "?topicId=" + Id(id) : ""), null, Access(accessToken), 200, ct);
}
