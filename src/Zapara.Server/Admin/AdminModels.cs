namespace Zapara.Server.Admin;

public sealed record AdminCommunityRow(Guid CommunityId, string Name, string Description, string? GroupId);
public sealed record AdminJoinRow(Guid RequestId, Guid CommunityId, Guid UserId, string Username, DateTimeOffset CreatedAt);
public sealed record AdminAccountRow(Guid UserId, string Username, string Status);
public sealed record AdminFamilyRow(Guid FamilyId, string DeviceName, string Platform, DateTimeOffset LastSeenAt, bool Revoked);
public sealed record AdminContentRow(string Kind, Guid ObjectId, Guid CommunityId, string Title, DateTimeOffset CreatedAt);
public sealed record AdminAuditRow(DateTimeOffset CreatedAt, string Action, string ObjectType, string ObjectId, string Outcome, Guid? ActorId);
