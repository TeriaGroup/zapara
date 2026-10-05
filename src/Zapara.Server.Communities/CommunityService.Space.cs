using Zapara.Contracts.Communities;
namespace Zapara.Server.Communities;
public sealed partial class CommunityService
{
    public Task<GroupSpaceResponse> SpaceAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.SpaceAsync(CommunityValidation.Id(communityId)), ct);
    public Task<GroupTopicListResponse> ArchivedTopicsAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.ArchivedTopicsAsync(CommunityValidation.Id(communityId)), ct);
    public Task<GroupSpaceResponse> SaveCategoryAsync(string bearer, Guid communityId, GroupCategoryRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.SaveCategoryAsync(CommunityValidation.Id(communityId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<GroupSpaceResponse> DeleteCategoryAsync(string bearer, Guid communityId, Guid categoryId, CancellationToken ct = default)
        => Run(bearer, db => db.DeleteCategoryAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(categoryId)), ct);
    public Task<GroupSpaceResponse> ArchiveTopicAsync(string bearer, Guid communityId, Guid topicId, GroupArchiveRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.ArchiveTopicAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(topicId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<GroupTopicAccessPreviewResponse> TopicAccessPreviewAsync(string bearer, Guid communityId, Guid topicId, GroupTopicAccessRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.TopicAccessPreviewAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(topicId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<GroupTopicAccessResponse> TopicAccessAsync(string bearer, Guid communityId, Guid topicId, CancellationToken ct = default)
        => Run(bearer, db => db.TopicAccessAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(topicId)), ct);
    public Task<GroupSpaceResponse> SetTopicAccessAsync(string bearer, Guid communityId, Guid topicId, GroupTopicAccessRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.SetTopicAccessAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(topicId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<GroupPermissionPreviewResponse> PreviewPermissionsAsync(string bearer, Guid communityId, GroupPermissionPreviewRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.PreviewPermissionsAsync(CommunityValidation.Id(communityId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<GroupAuditResponse> GroupAuditAsync(string bearer, Guid communityId, CancellationToken ct = default)
        => Run(bearer, db => db.GroupAuditAsync(CommunityValidation.Id(communityId)), ct);
    public Task<GroupDeskResponse> SaveRoleSettingsAsync(string bearer, Guid communityId, Guid roleId, GroupRoleSettingsRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.SaveRoleSettingsAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(roleId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<GroupRoleImpactResponse> RoleImpactAsync(string bearer, Guid communityId, Guid roleId, CancellationToken ct = default)
        => Run(bearer, db => db.RoleImpactAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(roleId)), ct);
    public Task<GroupFormListResponse> FormsAsync(string bearer, Guid communityId, Guid topicId, CancellationToken ct = default)
        => Run(bearer, db => db.FormsAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(topicId)), ct);
    public Task<GroupFormListResponse> CreateFormAsync(string bearer, Guid communityId, Guid topicId, GroupFormRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.CreateFormAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(topicId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<GroupFormResponse> SubmitFormAsync(string bearer, Guid communityId, Guid formId, GroupFormAnswerRequest request, CancellationToken ct = default)
        => Run(bearer, db => db.SubmitFormAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(formId), request ?? throw CommunityServiceException.InvalidRequest()), ct);
    public Task<GroupFormResponsesResponse> FormResponsesAsync(string bearer, Guid communityId, Guid formId, CancellationToken ct = default, Guid? after = null)
        => Run(bearer, db => db.FormResponsesAsync(CommunityValidation.Id(communityId), CommunityValidation.Id(formId), after), ct);
}
