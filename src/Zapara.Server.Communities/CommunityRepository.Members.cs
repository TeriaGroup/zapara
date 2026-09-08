using Npgsql;
using Zapara.Contracts.Communities;

namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository
{
    internal async Task<JoinRequestResponse> RequestJoinAsync(Guid communityId)
    {
        await LockCommunityAsync(communityId);
        var role = await ActiveRoleAsync(communityId);
        if (role is not null) throw CommunityServiceException.Conflict("already_member");
        if (await ExistsAsync($"SELECT request_id FROM {Schema}.join_requests WHERE community_id=@p0 AND user_id=@p1 AND status='pending'", communityId, UserId))
            throw CommunityServiceException.Conflict("already_requested");
        var id = Guid.NewGuid();
        try
        {
            await ExecuteAsync($"""
                INSERT INTO {Schema}.join_requests(request_id,community_id,user_id,status,created_at)
                VALUES(@p0,@p1,@p2,'pending',@p3)
                """, id, communityId, UserId, Now);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw CommunityServiceException.Conflict("already_requested"); }
        await AuditAsync(communityId, "join_requested", "join_request", id);
        return new(id, communityId, UserId, "pending", Now);
    }
    internal async Task<IReadOnlyList<JoinRequestResponse>> ListJoinRequestsAsync(Guid communityId)
    {
        await RequireStaffAsync(communityId);
        var list = new List<JoinRequestResponse>();
        await using var command = Command($"""
            SELECT request_id,community_id,user_id,status,created_at
            FROM {Schema}.join_requests WHERE community_id=@p0 AND status='pending'
            ORDER BY created_at, request_id
            """, communityId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
        return list;
    }
    internal Task<JoinRequestResponse> AcceptJoinAsync(Guid communityId, Guid requestId)
        => ResolveJoinAsync(communityId, requestId, accepted: true);
    internal Task<JoinRequestResponse> RejectJoinAsync(Guid communityId, Guid requestId)
        => ResolveJoinAsync(communityId, requestId, accepted: false);
    private async Task<JoinRequestResponse> ResolveJoinAsync(Guid communityId, Guid requestId, bool accepted)
    {
        await RequireStaffAsync(communityId);
        Guid userId;
        DateTimeOffset createdAt;
        await using (var command = Command($"""
            SELECT user_id,created_at,status FROM {Schema}.join_requests
            WHERE request_id=@p0 AND community_id=@p1 FOR UPDATE
            """, requestId, communityId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw CommunityServiceException.NotFound();
            if (reader.GetString(2) != "pending") throw CommunityServiceException.Conflict("revision_conflict");
            userId = reader.GetGuid(0);
            createdAt = reader.GetFieldValue<DateTimeOffset>(1);
        }
        var status = accepted ? "accepted" : "rejected";
        await ExecuteAsync($"""
            UPDATE {Schema}.join_requests SET status=@p0,resolved_at=@p1,resolved_by=@p2
            WHERE request_id=@p3
            """, status, Now, UserId, requestId);
        if (accepted)
        {
            await ExecuteAsync($"""
                INSERT INTO {Schema}.memberships AS m(community_id,user_id,role,status,created_at,revoked_at)
                VALUES(@p0,@p1,'member','active',@p2,NULL)
                ON CONFLICT (community_id,user_id) DO UPDATE
                SET role='member', status='active', revoked_at=NULL
                WHERE m.status='revoked'
                """, communityId, userId, Now);
        }
        await AuditAsync(communityId, accepted ? "join_accepted" : "join_rejected", "join_request", requestId);
        return new(requestId, communityId, userId, status, createdAt);
    }
    internal async Task<IReadOnlyList<MemberResponse>> ListMembersAsync(Guid communityId)
    {
        await RequireMemberAsync(communityId);
        var list = new List<MemberResponse>();
        await using var command = Command($"""
            SELECT user_id,role FROM {Schema}.memberships
            WHERE community_id=@p0 AND status='active' ORDER BY user_id
            """, communityId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return list;
    }
    internal async Task<IReadOnlyList<MemberResponse>> ListStaffAsync(Guid communityId)
    {
        await RequireMemberAsync(communityId);
        var list = new List<MemberResponse>();
        await using var command = Command($"""
            SELECT user_id,role FROM {Schema}.memberships
            WHERE community_id=@p0 AND status='active' AND role IN ('headman','curator')
            ORDER BY user_id
            """, communityId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return list;
    }
}
