using System.Security.Cryptography;

namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository
{
    // Full SHA-256 stays on the server; clients continue using the existing integer
    // topic revision. Including source rows instead of lifecycle hooks covers changes
    // made by administration, account deletion and collective actions alike.
    private string AccessSnapshotSql => $"""
        sha256(convert_to(jsonb_build_object(
            'version',1,
            'members',COALESCE((
                SELECT jsonb_agg(jsonb_build_array(m.user_id,m.role,m.status,u.status) ORDER BY m.user_id)
                FROM {Schema}.memberships m
                LEFT JOIN {configuration.Accounts.QuotedSchema}.users u ON u.user_id=m.user_id
                WHERE m.community_id=@p0
            ),'[]'::jsonb),
            'roles',COALESCE((
                SELECT jsonb_agg(jsonb_build_array(r.role_id,r.name,r.icon,r.position) ORDER BY r.role_id)
                FROM {Msg}.group_roles r WHERE r.community_id=@p0
            ),'[]'::jsonb),
            'grants',COALESCE((
                SELECT jsonb_agg(jsonb_build_array(g.role_id,g.user_id) ORDER BY g.role_id,g.user_id)
                FROM {Msg}.group_role_grants g JOIN {Msg}.group_roles r ON r.role_id=g.role_id
                WHERE r.community_id=@p0
            ),'[]'::jsonb),
            'powers',COALESCE((
                SELECT jsonb_agg(jsonb_build_array(p.role_id,p.power) ORDER BY p.role_id,p.power COLLATE "C")
                FROM {Msg}.group_role_powers p JOIN {Msg}.group_roles r ON r.role_id=p.role_id
                WHERE r.community_id=@p0
            ),'[]'::jsonb)
        )::text,'UTF8'))
        """;

    private async Task<byte[]> CurrentAccessSnapshotAsync(Guid communityId)
    {
        await using var command = Command($"SELECT {AccessSnapshotSql}", communityId);
        return (byte[])(await command.ExecuteScalarAsync(ct))!;
    }

    // Metadata GET is the refresh step after a conflict. No audit or private content
    // is written, and an unchanged digest never increments the revision again.
    private Task SynchronizeAccessRevisionsAsync(Guid communityId)
        => ExecuteAsync($"""
            WITH snapshot AS (SELECT {AccessSnapshotSql} AS value)
            UPDATE {Msg}.group_topics t SET revision=t.revision+1,access_snapshot=s.value
            FROM snapshot s WHERE t.community_id=@p0 AND t.access_snapshot IS DISTINCT FROM s.value
            """, communityId);

    private async Task RequireFreshAccessSnapshotAsync(Guid communityId, SpaceTopic topic)
    {
        var current = await CurrentAccessSnapshotAsync(communityId);
        if (topic.AccessSnapshot is null || !CryptographicOperations.FixedTimeEquals(topic.AccessSnapshot, current))
            throw CommunityServiceException.Conflict("revision_conflict");
    }

    // Re-evaluate every audience input in the UPDATE statement itself, rather than
    // trusting a prior SELECT or the UI's preview. This preserves the existing API.
    private Task<int> AdvanceAccessRevisionAsync(Guid communityId, Guid topicId, long expectedRevision)
        => ExecuteCountAsync($"""
            WITH snapshot AS (SELECT {AccessSnapshotSql} AS value)
            UPDATE {Msg}.group_topics SET revision=revision+1
            WHERE community_id=@p0 AND topic_id=@p1 AND revision=@p2
              AND access_snapshot=(SELECT value FROM snapshot)
            """, communityId, topicId, expectedRevision);
}
