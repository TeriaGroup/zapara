namespace Zapara.Contracts.Communities;

public sealed record GroupCapabilitiesResponse(int MaxRoles = 12, int MaxRolesPerMember = 3, int MaxTopics = 24,
    IReadOnlyList<string>? Powers = null, IReadOnlyList<string>? Templates = null);
public sealed record GroupCategoryRequest(Guid? CategoryId, string Title, int Position, long ExpectedRevision = 0);
public sealed record GroupCategoryResponse(Guid CategoryId, string Title, int Position, long Revision);
public sealed record GroupSpaceResponse(IReadOnlyList<GroupTopicResponse> Topics, IReadOnlyList<GroupCategoryResponse> Categories,
    GroupCapabilitiesResponse Capabilities, GroupDeskResponse Desk);
public sealed record GroupArchiveRequest(bool Archived, long ExpectedRevision);
public sealed record GroupAccessRule(Guid? RoleId, string Power, string State);
public sealed record GroupTopicAccessRequest(IReadOnlyList<GroupAccessRule> Rules, long ExpectedRevision);
public sealed record GroupTopicAccessResponse(Guid TopicId, long Revision, IReadOnlyList<GroupAccessRule> Rules);
public sealed record GroupTopicAccessPreviewParticipantResponse(Guid UserId, IReadOnlyList<string> BeforePermissions,
    IReadOnlyList<string> AfterPermissions, IReadOnlyDictionary<string, string> Sources);
public sealed record GroupTopicAccessPreviewResponse(Guid TopicId, long Revision, int AffectedCount,
    IReadOnlyList<Guid> BeforeReaders, IReadOnlyList<Guid> AfterReaders, IReadOnlyList<GroupTopicAccessPreviewParticipantResponse> Participants);
public sealed record GroupPermissionPreviewRequest(Guid? UserId, Guid? RoleId);
public sealed record GroupPermissionPreviewResponse(IReadOnlyList<GroupTopicResponse> Topics);
public sealed record GroupAuditEventResponse(Guid EventId, Guid? ActorId, string Action, Guid ObjectId, DateTimeOffset CreatedAt);
public sealed record GroupAuditResponse(IReadOnlyList<GroupAuditEventResponse> Events);
public sealed record GroupRoleSettingsRequest(string Name, string Icon, int Position, long ExpectedRevision);
public sealed record GroupRoleImpactResponse(Guid RoleId, int Assignments, int AccessRules);

public sealed record GroupFormQuestion(Guid QuestionId, string Title, string Kind, bool Required, IReadOnlyList<string> Options);
public sealed record GroupFormRequest(string Title, string Description, DateTimeOffset? DeadlineAt, bool Anonymous, IReadOnlyList<GroupFormQuestion> Questions);
public sealed record GroupFormAnswer(Guid QuestionId, string? Text, IReadOnlyList<string> Choices);
public sealed record GroupFormAnswerRequest(IReadOnlyList<GroupFormAnswer> Answers);
public sealed record GroupFormAnswerResponse(Guid? RespondentId, IReadOnlyList<GroupFormAnswer> Answers, DateTimeOffset UpdatedAt);
public sealed record GroupFormResponse(Guid FormId, Guid TopicId, string Title, string Description, DateTimeOffset? DeadlineAt,
    bool Anonymous, IReadOnlyList<GroupFormQuestion> Questions, Guid CreatedBy, DateTimeOffset CreatedAt,
    bool CanRespond, bool CanViewResponses, GroupFormAnswerResponse? OwnResponse, int ResponseCount);
public sealed record GroupFormListResponse(IReadOnlyList<GroupFormResponse> Forms);
public sealed record GroupFormResponsesResponse(Guid FormId, IReadOnlyList<GroupFormAnswerResponse> Responses, Guid? NextCursor = null, int TotalResponses = 0);
