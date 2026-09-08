using System.Security.Claims;
using Npgsql;
using Zapara.Contracts.Communities;
using Zapara.Server.Accounts;

namespace Zapara.Server.Admin;

public sealed class AdminService(AccountsDataSource dataSource, AdminConfiguration configuration,
    AdminAuthService auth, TimeProvider clock)
{
    public Task<T> ExecuteAsync<T>(ClaimsPrincipal principal, Func<AdminWork, Task<T>> operation,
        bool requireReauth, string? password, CancellationToken ct)
        => RunAsync(principal, requireReauth, password, operation, ct);

    public Task ExecuteAsync(ClaimsPrincipal principal, Func<AdminWork, Task> operation,
        bool requireReauth, string? password, CancellationToken ct)
        => RunAsync(principal, requireReauth, password, async work => { await operation(work); return 0; }, ct);

    private async Task<T> RunAsync<T>(ClaimsPrincipal principal, bool requireReauth, string? password,
        Func<AdminWork, Task<T>> operation, CancellationToken ct)
    {
        if (!Guid.TryParseExact(principal.FindFirstValue(ClaimTypes.NameIdentifier), "D", out var userId) ||
            !Guid.TryParseExact(principal.FindFirstValue("sid"), "D", out var sessionId) ||
            !long.TryParse(principal.FindFirstValue("cv"), out var version))
            throw AdminException.Unauthorized();
        try
        {
            await using var connection = dataSource.CreateConnection();
            await connection.OpenAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(ct);
            var now = clock.GetUtcNow().ToUniversalTime();
            now = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
            var work = new AdminWork(connection, tx, configuration, userId, sessionId, version, now, ct);
            await work.LockActorAsync();
            if (requireReauth) await work.RequireReauthAsync(auth, password);
            var result = await operation(work);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (AdminException) { throw; }
        catch (ArgumentException) { throw AdminException.Invalid(); }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            throw e.SqlState == PostgresErrorCodes.UniqueViolation ? AdminException.Conflict("conflict") : AdminException.NotFound();
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            throw AdminException.Unavailable();
        }
    }
}

public sealed class AdminWork(NpgsqlConnection connection, NpgsqlTransaction tx, AdminConfiguration configuration,
    Guid userId, Guid sessionId, long credentialVersion, DateTimeOffset now, CancellationToken ct)
{
    private string Adm => configuration.QuotedSchema;
    private string Acc => configuration.Accounts.QuotedSchema;
    private string Com => configuration.Communities.QuotedSchema;

    internal async Task LockActorAsync()
    {
        await using var user = Command($"SELECT status,credential_version FROM {Acc}.users WHERE user_id=@p0 FOR UPDATE", userId);
        await using var reader = await user.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.GetString(0) != "active" || reader.GetInt64(1) != credentialVersion)
            throw AdminException.Unauthorized();
        await reader.DisposeAsync();
        await using var admin = Command($"SELECT 1 FROM {Adm}.platform_admins WHERE user_id=@p0 AND revoked_at IS NULL FOR UPDATE", userId);
        if (await admin.ExecuteScalarAsync(ct) is null) throw AdminException.Unauthorized();
        await using var session = Command($"""
            SELECT revoked_at,expires_at FROM {Adm}.admin_sessions WHERE session_id=@p0 AND user_id=@p1 FOR UPDATE
            """, sessionId, userId);
        await using var sessionReader = await session.ExecuteReaderAsync(ct);
        if (!await sessionReader.ReadAsync(ct) || !sessionReader.IsDBNull(0) || now >= sessionReader.GetFieldValue<DateTimeOffset>(1))
            throw AdminException.Unauthorized();
    }

    internal async Task RequireReauthAsync(AdminAuthService auth, string? password)
    {
        DateTimeOffset? until;
        await using (var command = Command($"SELECT reauth_until FROM {Adm}.admin_sessions WHERE session_id=@p0", sessionId))
            until = await command.ExecuteScalarAsync(ct) as DateTimeOffset?;
        if (until is { } live && live > now) return;
        if (string.IsNullOrEmpty(password)) throw AdminException.Reauth();
        await auth.VerifyPasswordAsync(userId, password, credentialVersion, ct);
        await Exec($"UPDATE {Adm}.admin_sessions SET reauth_until=@p0 WHERE session_id=@p1", now.Add(AdminDefaults.ReauthLifetime), sessionId);
        await Audit("reauth", "admin_session", sessionId.ToString("D"));
    }

    public async Task<IReadOnlyList<AdminCommunityRow>> ListCommunitiesAsync()
    {
        var list = new List<AdminCommunityRow>();
        await using var command = Command($"""
            SELECT c.community_id,c.name,c.description,m.group_id
            FROM {Com}.communities c
            LEFT JOIN {Com}.catalog_maps m ON m.community_id=c.community_id
            ORDER BY c.name, c.community_id
            """);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        return list;
    }

    public async Task CreateCommunityAsync(string name, string description)
    {
        name = CommunityValidation.Name(name);
        description = CommunityValidation.Description(description);
        var id = Guid.NewGuid();
        await Exec($"""
            INSERT INTO {Com}.communities(community_id,name,description,revision,created_at,updated_at)
            VALUES(@p0,@p1,@p2,1,@p3,@p3)
            """, id, name, description, now);
        await Audit("community_created", "community", id.ToString("D"));
    }

    public async Task MapCatalogAsync(Guid communityId, string groupId, string groupName)
    {
        communityId = CommunityValidation.Id(communityId);
        groupId = CommunityValidation.GroupId(groupId);
        groupName = CommunityValidation.Name(groupName);
        await LockCommunityAsync(communityId);
        var mapId = Guid.NewGuid();
        await Exec($"""
            INSERT INTO {Com}.catalog_maps(map_id,community_id,group_id,group_name,created_at)
            VALUES(@p0,@p1,@p2,@p3,@p4)
            """, mapId, communityId, groupId, groupName, now);
        await CommunityAudit(communityId, "catalog_mapped", "catalog_map", mapId);
        await Audit("catalog_mapped", "catalog_map", mapId.ToString("D"));
    }

    public async Task AssignStaffAsync(Guid communityId, Guid targetUserId, string role)
    {
        communityId = CommunityValidation.Id(communityId);
        targetUserId = CommunityValidation.Id(targetUserId);
        role = CommunityValidation.StaffRole(role);
        await LockCommunityAsync(communityId);
        await Exec($"""
            INSERT INTO {Com}.memberships AS m(community_id,user_id,role,status,created_at,revoked_at)
            VALUES(@p0,@p1,@p2,'active',@p3,NULL)
            ON CONFLICT (community_id,user_id) DO UPDATE
            SET role=@p2, status='active', revoked_at=NULL
            """, communityId, targetUserId, role, now);
        await Exec($"""
            UPDATE {Com}.staff_assignments SET revoked_at=@p0
            WHERE community_id=@p1 AND user_id=@p2 AND revoked_at IS NULL
            """, now, communityId, targetUserId);
        var assignmentId = Guid.NewGuid();
        await Exec($"""
            INSERT INTO {Com}.staff_assignments(assignment_id,community_id,user_id,role,assigned_at,revoked_at)
            VALUES(@p0,@p1,@p2,@p3,@p4,NULL)
            """, assignmentId, communityId, targetUserId, role, now);
        await CommunityAudit(communityId, "staff_assigned", "staff_assignment", assignmentId);
        await Audit("staff_assigned", "staff_assignment", assignmentId.ToString("D"));
    }

    public async Task<IReadOnlyList<AdminJoinRow>> ListJoinRequestsAsync()
    {
        var list = new List<AdminJoinRow>();
        await using var command = Command($"""
            SELECT r.request_id,r.community_id,r.user_id,u.username,r.created_at
            FROM {Com}.join_requests r
            JOIN {Acc}.users u ON u.user_id=r.user_id
            WHERE r.status='pending'
            ORDER BY r.created_at, r.request_id
            """);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
        return list;
    }

    public Task AcceptJoinAsync(Guid communityId, Guid requestId) => ResolveJoinAsync(communityId, requestId, true);
    public Task RejectJoinAsync(Guid communityId, Guid requestId) => ResolveJoinAsync(communityId, requestId, false);

    private async Task ResolveJoinAsync(Guid communityId, Guid requestId, bool accepted)
    {
        communityId = CommunityValidation.Id(communityId);
        requestId = CommunityValidation.Id(requestId);
        await LockCommunityAsync(communityId);
        Guid target;
        await using (var command = Command($"""
            SELECT user_id,status FROM {Com}.join_requests WHERE request_id=@p0 AND community_id=@p1 FOR UPDATE
            """, requestId, communityId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw AdminException.NotFound();
            if (reader.GetString(1) != "pending") throw AdminException.Conflict("revision_conflict");
            target = reader.GetGuid(0);
        }
        var status = accepted ? "accepted" : "rejected";
        await Exec($"""
            UPDATE {Com}.join_requests SET status=@p0,resolved_at=@p1,resolved_by=@p2 WHERE request_id=@p3
            """, status, now, userId, requestId);
        if (accepted)
        {
            await Exec($"""
                INSERT INTO {Com}.memberships AS m(community_id,user_id,role,status,created_at,revoked_at)
                VALUES(@p0,@p1,'member','active',@p2,NULL)
                ON CONFLICT (community_id,user_id) DO UPDATE
                SET role='member', status='active', revoked_at=NULL
                WHERE m.status='revoked'
                """, communityId, target, now);
        }
        await CommunityAudit(communityId, accepted ? "join_accepted" : "join_rejected", "join_request", requestId);
        await Audit(accepted ? "join_accepted" : "join_rejected", "join_request", requestId.ToString("D"));
    }

    public async Task<IReadOnlyList<AdminAccountRow>> ListAccountsAsync()
    {
        var list = new List<AdminAccountRow>();
        await using var command = Command($"""
            SELECT user_id,username,status FROM {Acc}.users ORDER BY created_at DESC, user_id LIMIT 100
            """);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        return list;
    }

    public async Task<IReadOnlyList<AdminFamilyRow>> ListFamiliesAsync(Guid targetUserId)
    {
        targetUserId = CommunityValidation.Id(targetUserId);
        var list = new List<AdminFamilyRow>();
        await using var command = Command($"""
            SELECT family_id,device_name,platform,last_seen_at,revoked_at IS NOT NULL
            FROM {Acc}.session_families WHERE user_id=@p0 ORDER BY last_seen_at DESC
            """, targetUserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3), reader.GetBoolean(4)));
        return list;
    }

    public async Task DisableAccountAsync(Guid targetUserId)
    {
        targetUserId = CommunityValidation.Id(targetUserId);
        if (targetUserId == userId) throw AdminException.Invalid();
        await using (var lockUser = Command($"SELECT status FROM {Acc}.users WHERE user_id=@p0 FOR UPDATE", targetUserId))
        {
            var status = await lockUser.ExecuteScalarAsync(ct) as string;
            if (status is null) throw AdminException.NotFound();
        }
        await Exec($"UPDATE {Acc}.users SET status='disabled' WHERE user_id=@p0", targetUserId);
        await Exec($"""
            UPDATE {Acc}.session_families SET revoked_at=@p0,revocation_reason='disabled'
            WHERE user_id=@p1 AND revoked_at IS NULL
            """, now, targetUserId);
        await Exec($"UPDATE {Adm}.admin_sessions SET revoked_at=@p0 WHERE user_id=@p1 AND revoked_at IS NULL", now, targetUserId);
        await Audit("account_disabled", "account", targetUserId.ToString("D"));
    }

    public async Task RevokeFamilyAsync(Guid familyId)
    {
        familyId = CommunityValidation.Id(familyId);
        Guid owner;
        await using (var command = Command($"SELECT user_id FROM {Acc}.session_families WHERE family_id=@p0 FOR UPDATE", familyId))
        {
            var raw = await command.ExecuteScalarAsync(ct);
            if (raw is null or DBNull) throw AdminException.NotFound();
            owner = (Guid)raw;
        }
        await Exec($"""
            UPDATE {Acc}.session_families SET revoked_at=@p0,revocation_reason='revoke'
            WHERE family_id=@p1 AND revoked_at IS NULL
            """, now, familyId);
        await Audit("session_revoked", "session_family", familyId.ToString("D"));
        _ = owner;
    }

    public async Task<IReadOnlyList<AdminContentRow>> ListContentAsync()
    {
        var list = new List<AdminContentRow>();
        await using var command = Command($"""
            SELECT kind,object_id,community_id,title,created_at FROM (
              SELECT 'shared_homework' kind, homework_id object_id, community_id, title, created_at FROM {Com}.shared_homework
              UNION ALL
              SELECT 'announcement', announcement_id, community_id, title, created_at FROM {Com}.announcements
              UNION ALL
              SELECT 'poll', poll_id, community_id, question, created_at FROM {Com}.polls
            ) content ORDER BY created_at DESC, object_id LIMIT 200
            """);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new(reader.GetString(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
        return list;
    }

    public async Task ModerateAsync(string kind, Guid objectId, Guid communityId)
    {
        objectId = CommunityValidation.Id(objectId);
        communityId = CommunityValidation.Id(communityId);
        await LockCommunityAsync(communityId);
        if (kind == "shared_homework")
        {
            await Exec($"DELETE FROM {Com}.shared_homework_completion WHERE homework_id=@p0", objectId);
            var deleted = await Exec($"DELETE FROM {Com}.shared_homework WHERE homework_id=@p0 AND community_id=@p1", objectId, communityId);
            if (deleted == 0) throw AdminException.NotFound();
        }
        else if (kind == "announcement")
        {
            var deleted = await Exec($"DELETE FROM {Com}.announcements WHERE announcement_id=@p0 AND community_id=@p1", objectId, communityId);
            if (deleted == 0) throw AdminException.NotFound();
        }
        else if (kind == "poll")
        {
            await Exec($"DELETE FROM {Com}.votes WHERE poll_id=@p0", objectId);
            var deleted = await Exec($"DELETE FROM {Com}.polls WHERE poll_id=@p0 AND community_id=@p1", objectId, communityId);
            if (deleted == 0) throw AdminException.NotFound();
        }
        else throw AdminException.Invalid();
        await Audit("content_moderated", kind, objectId.ToString("D"));
    }

    public async Task<IReadOnlyList<AdminAuditRow>> ListAuditAsync()
    {
        var list = new List<AdminAuditRow>();
        await using var command = Command($"""
            SELECT created_at,action,object_type,object_id,outcome,actor_id
            FROM {Adm}.admin_audit ORDER BY created_at DESC, event_id DESC LIMIT 200
            """);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new(reader.GetFieldValue<DateTimeOffset>(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetGuid(5)));
        return list;
    }

    private async Task LockCommunityAsync(Guid communityId)
    {
        await using var command = Command($"SELECT community_id FROM {Com}.communities WHERE community_id=@p0 FOR UPDATE", communityId);
        if (await command.ExecuteScalarAsync(ct) is null) throw AdminException.NotFound();
    }

    private Task CommunityAudit(Guid communityId, string action, string objectType, Guid objectId)
        => Exec($"""
            INSERT INTO {Com}.community_audit(event_id,community_id,actor_id,action,object_type,object_id,outcome,created_at)
            VALUES(@p0,@p1,@p2,@p3,@p4,@p5,'success',@p6)
            """, Guid.NewGuid(), communityId, userId, action, objectType, objectId, now);

    private Task Audit(string action, string objectType, string objectId, string outcome = "success")
        => Exec($"""
            INSERT INTO {Adm}.admin_audit(event_id,actor_id,action,object_type,object_id,outcome,created_at)
            VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)
            """, Guid.NewGuid(), userId, action, objectType, objectId, outcome, now);

    private NpgsqlCommand Command(string sql, params object?[] values)
    {
        var command = new NpgsqlCommand(sql, connection, tx);
        for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("p" + i, values[i] ?? DBNull.Value);
        return command;
    }

    private async Task<int> Exec(string sql, params object?[] values)
    {
        await using var command = Command(sql, values);
        return await command.ExecuteNonQueryAsync(ct);
    }
}
