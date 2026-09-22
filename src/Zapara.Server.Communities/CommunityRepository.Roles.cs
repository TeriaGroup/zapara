using Npgsql;
using Zapara.Contracts.Communities;

namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository
{
    internal async Task<GroupDeskResponse> DeskAsync(Guid communityId)
    {
        var role = await RequireMemberAsync(communityId);
        var headman = role == "headman";
        var roles = new List<GroupRoleResponse>();
        await using (var command = Command($"SELECT role_id, name FROM {Msg}.group_roles WHERE community_id=@p0 ORDER BY name, role_id", communityId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) roles.Add(new(reader.GetGuid(0), reader.GetString(1)));
        var grants = new List<GroupGrantResponse>();
        await using (var command = Command($"""
            SELECT g.role_id, g.user_id FROM {Msg}.group_role_grants g
            JOIN {Msg}.group_roles r ON r.role_id=g.role_id
            WHERE r.community_id=@p0 ORDER BY g.user_id, g.role_id
            """, communityId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) grants.Add(new(reader.GetGuid(0), reader.GetGuid(1)));
        var powers = new List<GroupPowerResponse>();
        await using (var command = Command($"""
            SELECT p.role_id, p.power FROM {Msg}.group_role_powers p
            JOIN {Msg}.group_roles r ON r.role_id=p.role_id
            WHERE r.community_id=@p0 ORDER BY p.role_id, p.power
            """, communityId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) powers.Add(new(reader.GetGuid(0), reader.GetString(1)));
        var mine = new List<string>();
        if (headman) mine.AddRange(GroupChanges.Powers);
        else
        {
            await using var command = Command($"""
                SELECT DISTINCT p.power FROM {Msg}.group_role_powers p
                JOIN {Msg}.group_role_grants g ON g.role_id=p.role_id
                JOIN {Msg}.group_roles r ON r.role_id=p.role_id
                WHERE r.community_id=@p0 AND g.user_id=@p1
                ORDER BY p.power
                """, communityId, UserId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) mine.Add(reader.GetString(0));
            if (role == "curator" && !mine.Contains("joins")) mine.Add("joins");
        }
        var applicants = new List<GroupApplicantResponse>();
        if (mine.Contains("joins"))
        {
            await using var command = Command($"""
                SELECT j.request_id, j.user_id, u.username, u.display_name
                FROM {Schema}.join_requests j
                JOIN {configuration.Accounts.QuotedSchema}.users u ON u.user_id=j.user_id
                WHERE j.community_id=@p0 AND j.status='pending'
                ORDER BY j.created_at, j.request_id
                """, communityId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                applicants.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return new(headman, roles, grants, applicants, powers, mine);
    }

    internal async Task<GroupDeskResponse> CreateRoleAsync(Guid communityId, string? rawName)
    {
        await RequirePowerAsync(communityId, "roles");
        var name = GroupRoleNames.Clean(rawName) ?? throw CommunityServiceException.InvalidRequest();
        if (await ScalarAsync($"SELECT count(*)::int FROM {Msg}.group_roles WHERE community_id=@p0", communityId) is >= 12)
            throw CommunityServiceException.InvalidRequest();
        var id = Guid.NewGuid();
        try
        {
            await ExecuteAsync($"INSERT INTO {Msg}.group_roles(role_id,community_id,name,created_at) VALUES(@p0,@p1,@p2,@p3)", id, communityId, name, Now);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw CommunityServiceException.Conflict("revision_conflict"); }
        return await DeskAsync(communityId);
    }

    internal async Task<GroupDeskResponse> RenameRoleAsync(Guid communityId, Guid roleId, string? rawName)
    {
        await RequirePowerAsync(communityId, "roles");
        var name = GroupRoleNames.Clean(rawName) ?? throw CommunityServiceException.InvalidRequest();
        int updated;
        try
        {
            updated = await ExecuteCountAsync($"""
                UPDATE {Msg}.group_roles SET name=@p0 WHERE role_id=@p1 AND community_id=@p2
                """, name, roleId, communityId);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw CommunityServiceException.Conflict("revision_conflict"); }
        if (updated != 1) throw CommunityServiceException.NotFound();
        return await DeskAsync(communityId);
    }

    internal async Task<GroupDeskResponse> DeleteRoleAsync(Guid communityId, Guid roleId)
    {
        await RequirePowerAsync(communityId, "roles");
        var updated = await ExecuteCountAsync($"DELETE FROM {Msg}.group_roles WHERE role_id=@p0 AND community_id=@p1", roleId, communityId);
        if (updated != 1) throw CommunityServiceException.NotFound();
        return await DeskAsync(communityId);
    }

    internal async Task<GroupDeskResponse> GrantRoleAsync(Guid communityId, Guid roleId, Guid userId)
    {
        await RequirePowerAsync(communityId, "grants");
        if (!await ExistsAsync($"SELECT role_id FROM {Msg}.group_roles WHERE role_id=@p0 AND community_id=@p1", roleId, communityId))
            throw CommunityServiceException.NotFound();
        if (!await ExistsAsync($"SELECT user_id FROM {Schema}.memberships WHERE community_id=@p0 AND user_id=@p1 AND status='active'", communityId, userId))
            throw CommunityServiceException.NotFound();
        var held = await ScalarAsync($"""
            SELECT count(*)::int FROM {Msg}.group_role_grants g
            JOIN {Msg}.group_roles r ON r.role_id=g.role_id
            WHERE r.community_id=@p0 AND g.user_id=@p1 AND g.role_id<>@p2
            """, communityId, userId, roleId);
        if (held is >= 3) throw CommunityServiceException.InvalidRequest();
        await ExecuteAsync($"""
            INSERT INTO {Msg}.group_role_grants(role_id,user_id) VALUES(@p0,@p1) ON CONFLICT DO NOTHING
            """, roleId, userId);
        return await DeskAsync(communityId);
    }

    internal async Task<GroupDeskResponse> RevokeRoleAsync(Guid communityId, Guid roleId, Guid userId)
    {
        await RequirePowerAsync(communityId, "grants");
        await ExecuteAsync($"""
            DELETE FROM {Msg}.group_role_grants g USING {Msg}.group_roles r
            WHERE g.role_id=r.role_id AND g.role_id=@p0 AND g.user_id=@p1 AND r.community_id=@p2
            """, roleId, userId, communityId);
        return await DeskAsync(communityId);
    }

    internal async Task<GroupDeskResponse> RemoveMemberAsync(Guid communityId, Guid userId)
    {
        await RequirePowerAsync(communityId, "exclude");
        var updated = await ExecuteCountAsync($"""
            UPDATE {Schema}.memberships SET status='revoked', revoked_at=@p0
            WHERE community_id=@p1 AND user_id=@p2 AND status='active' AND role='member'
            """, Now, communityId, userId);
        if (updated != 1) throw CommunityServiceException.NotFound();
        await ExecuteAsync($"""
            DELETE FROM {Msg}.group_role_grants g USING {Msg}.group_roles r
            WHERE g.role_id=r.role_id AND r.community_id=@p0 AND g.user_id=@p1
            """, communityId, userId);
        await ExecuteAsync($"""
            DELETE FROM {Msg}.conversation_members m USING {Msg}.conversations c
            WHERE m.conversation_id=c.conversation_id AND c.kind='group' AND c.community_id=@p0 AND m.user_id=@p1
            """, communityId, userId);
        return await DeskAsync(communityId);
    }

    private async Task RequireHeadmanAsync(Guid communityId)
    {
        if (await RequireMemberAsync(communityId) != "headman") throw CommunityServiceException.Forbidden();
    }

    private async Task<int> ScalarAsync(string sql, params object?[] parameters)
    {
        await using var command = Command(sql, parameters);
        return await command.ExecuteScalarAsync(ct) switch
        {
            int value => value,
            long value => (int)value,
            _ => 0
        };
    }

    private async Task<int> ExecuteCountAsync(string sql, params object?[] parameters)
    {
        await using var command = Command(sql, parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }
}
