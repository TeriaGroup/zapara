using Npgsql;
using Zapara.Contracts.Communities;
using Zapara.Server.Accounts;

namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository(TrustedAccountContext context, CommunitiesConfiguration configuration, CancellationToken ct)
{
    private string Schema => configuration.QuotedSchema;
    private Guid UserId => context.UserId;
    // PostgreSQL timestamps have microsecond precision; compare and store the same truncated instant.
    private DateTimeOffset Now => new(context.UtcNow.Ticks - context.UtcNow.Ticks % 10, TimeSpan.Zero);
    private NpgsqlCommand Command(string sql, params object?[] parameters)
    {
        var command = new NpgsqlCommand(sql, context.Connection, context.Transaction);
        for (var i = 0; i < parameters.Length; i++) command.Parameters.AddWithValue("p" + i, parameters[i] ?? DBNull.Value);
        return command;
    }
    private async Task ExecuteAsync(string sql, params object?[] parameters)
    {
        await using var command = Command(sql, parameters);
        await command.ExecuteNonQueryAsync(ct);
    }
    private async Task<bool> ExistsAsync(string sql, params object?[] parameters)
    {
        await using var command = Command(sql, parameters);
        return await command.ExecuteScalarAsync(ct) is not null and not DBNull;
    }
    internal async Task LockCommunityAsync(Guid communityId)
    {
        await using var command = Command($"SELECT community_id FROM {Schema}.communities WHERE community_id=@p0 FOR UPDATE", communityId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw CommunityServiceException.NotFound();
    }
    internal async Task<string?> ActiveRoleAsync(Guid communityId)
    {
        await using var command = Command($"""
            SELECT role FROM {Schema}.memberships
            WHERE community_id=@p0 AND user_id=@p1 AND status='active' FOR UPDATE
            """, communityId, UserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? reader.GetString(0) : null;
    }
    internal async Task<string> RequireMemberAsync(Guid communityId)
    {
        await LockCommunityAsync(communityId);
        return await ActiveRoleAsync(communityId) ?? throw CommunityServiceException.Forbidden();
    }
    internal async Task<string> RequireStaffAsync(Guid communityId)
    {
        var role = await RequireMemberAsync(communityId);
        if (role is not ("headman" or "curator")) throw CommunityServiceException.Forbidden();
        return role;
    }
    internal async Task AuditAsync(Guid communityId, string action, string objectType, Guid objectId, string outcome = "success")
        => await ExecuteAsync($"""
            INSERT INTO {Schema}.community_audit(event_id,community_id,actor_id,action,object_type,object_id,outcome,created_at)
            VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7)
            """, Guid.NewGuid(), communityId, UserId, action, objectType, objectId, outcome, Now);
    internal async Task<IReadOnlyList<CommunityResponse>> ListMembershipsAsync()
    {
        var list = new List<CommunityResponse>();
        await using var command = Command($"""
            SELECT c.community_id,c.name,c.description,c.revision,m.role
            FROM {Schema}.memberships m
            JOIN {Schema}.communities c ON c.community_id=m.community_id
            WHERE m.user_id=@p0 AND m.status='active'
            ORDER BY c.name, c.community_id
            """, UserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(ReadCommunity(reader, reader.GetString(4)));
        return list;
    }
    internal async Task<IReadOnlyList<CommunityResponse>> LookupGroupAsync(string groupId)
    {
        var list = new List<CommunityResponse>();
        await using var command = Command($"""
            SELECT c.community_id,c.name,c.description,c.revision,m.role
            FROM {Schema}.catalog_maps map
            JOIN {Schema}.communities c ON c.community_id=map.community_id
            LEFT JOIN {Schema}.memberships m ON m.community_id=c.community_id AND m.user_id=@p0 AND m.status='active'
            WHERE map.group_id=@p1
            """, UserId, groupId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            list.Add(ReadCommunity(reader, reader.IsDBNull(4) ? null : reader.GetString(4)));
        return list;
    }
    internal async Task<CommunityResponse> GetAsync(Guid communityId)
    {
        await using var command = Command($"""
            SELECT c.community_id,c.name,c.description,c.revision,m.role
            FROM {Schema}.communities c
            LEFT JOIN {Schema}.memberships m ON m.community_id=c.community_id AND m.user_id=@p0 AND m.status='active'
            WHERE c.community_id=@p1
            """, UserId, communityId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw CommunityServiceException.NotFound();
        return ReadCommunity(reader, reader.IsDBNull(4) ? null : reader.GetString(4));
    }
    private static CommunityResponse ReadCommunity(NpgsqlDataReader reader, string? role)
        => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), role);
}
