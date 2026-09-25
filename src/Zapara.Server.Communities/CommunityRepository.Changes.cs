using Npgsql;
using Zapara.Contracts.Communities;

namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository
{
    internal async Task<GroupDeskResponse> SetRolePowerAsync(Guid communityId, Guid roleId, GroupPowerRequest request)
    {
        await RequireHeadmanAsync(communityId);
        if (request is null || !GroupChanges.KnownPower(request.Power)) throw CommunityServiceException.InvalidRequest();
        if (!await WritePowerAsync(communityId, roleId, request.Power, request.Enabled)) throw CommunityServiceException.NotFound();
        return await DeskAsync(communityId);
    }

    internal async Task<BallotBoardResponse> ProposeChangeAsync(Guid communityId, BallotChangeRequest request)
    {
        if (request is null) throw CommunityServiceException.InvalidRequest();
        await RequireMemberAsync(communityId);
        await RequireBallotPublishTopicAsync(communityId, request.TopicId);
        await CloseExpiredAsync(communityId);
        if (await ActiveCountAsync(communityId) >= BallotRules.ActiveLimit) throw CommunityServiceException.InvalidRequest();
        var pending = await ScalarAsync($"""
            SELECT count(*)::int FROM {Msg}.ballots
            WHERE community_id=@p0 AND created_by=@p1 AND origin='collective' AND status='collecting'
            """, communityId, UserId);
        if (pending >= 1) throw CommunityServiceException.InvalidRequest();
        var payload = GroupChanges.Canonical(request.Kind, request.RoleId, request.UserId, request.Name, request.Power, request.Enabled)
            ?? throw CommunityServiceException.InvalidRequest();
        var change = GroupChanges.Read(payload) ?? throw CommunityServiceException.InvalidRequest();
        var roleName = "";
        var personName = "";
        if (change.Kind is "power" or "grant" or "revoke_grant" or "rename_role" or "delete_role")
            roleName = await RoleLabelAsync(communityId, change.RoleId) ?? throw CommunityServiceException.InvalidRequest();
        if (change.Kind is "grant" or "revoke_grant" or "remove_member")
            personName = await MemberLabelAsync(communityId, change.UserId, change.Kind == "remove_member") ?? throw CommunityServiceException.InvalidRequest();
        if (change.Kind == "create_role")
        {
            if (await ScalarAsync($"SELECT count(*)::int FROM {Msg}.group_roles WHERE community_id=@p0", communityId) >= 12)
                throw CommunityServiceException.InvalidRequest();
            if (await ExistsAsync($"SELECT role_id FROM {Msg}.group_roles WHERE community_id=@p0 AND lower(name)=lower(@p1)", communityId, change.Name))
                throw CommunityServiceException.InvalidRequest();
        }
        if (change.Kind == "rename_role" && await ExistsAsync($"""
            SELECT role_id FROM {Msg}.group_roles WHERE community_id=@p0 AND role_id<>@p1 AND lower(name)=lower(@p2)
            """, communityId, change.RoleId, change.Name))
            throw CommunityServiceException.InvalidRequest();
        string question;
        try { question = CommunityValidation.Question(GroupChanges.Question(change, roleName, personName)); }
        catch (ArgumentException) { throw CommunityServiceException.InvalidRequest(); }
        var id = await InsertBallotAsync(communityId, question, new[] { "Принять", "Отклонить" }, "collective", "collecting", Now.AddDays(request.Days), UserId, request.TopicId);
        await ExecuteAsync($"""
            INSERT INTO {Msg}.ballot_effects(ballot_id,kind,payload) VALUES(@p0,@p1,@p2)
            """, id, change.Kind, payload);
        await ExecuteAsync($"""
            INSERT INTO {Msg}.ballot_support(ballot_id,user_id,created_at) VALUES(@p0,@p1,@p2)
            ON CONFLICT DO NOTHING
            """, id, UserId, Now);
        return await ReadBoardAsync(communityId);
    }

    private async Task ApplyDueAsync(Guid communityId)
    {
        var need = BallotRules.SupportersNeeded(await ScalarAsync($"SELECT count(*)::int FROM {Schema}.memberships WHERE community_id=@p0 AND status='active'", communityId));
        var due = new List<(Guid Id, string Payload)>();
        await using (var command = Command($"""
            SELECT effect.ballot_id, effect.payload
            FROM {Msg}.ballot_effects AS effect
            JOIN {Msg}.ballots AS ballot ON ballot.ballot_id=effect.ballot_id
            WHERE ballot.community_id=@p0 AND ballot.origin='collective' AND ballot.status='closed' AND effect.applied_at IS NULL
            ORDER BY ballot.created_at, effect.ballot_id
            """, communityId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) due.Add((reader.GetGuid(0), reader.GetString(1)));
        foreach (var item in due)
        {
            var yes = await VotesAsync(item.Id, 0);
            var no = await VotesAsync(item.Id, 1);
            var outcome = !GroupChanges.Passes(yes, no, need) ? "rejected" : await TryApplyAsync(communityId, item.Payload) ? "accepted" : "skipped";
            await ExecuteAsync($"""
                UPDATE {Msg}.ballot_effects SET outcome=@p0, applied_at=@p1
                WHERE ballot_id=@p2 AND applied_at IS NULL
                """, outcome, Now, item.Id);
        }
    }

    private async Task<bool> TryApplyAsync(Guid communityId, string payload)
    {
        var change = GroupChanges.Read(payload);
        if (change is null) return false;
        try
        {
            return change.Kind switch
            {
                "power" => await WritePowerAsync(communityId, change.RoleId, change.Power, change.Enabled),
                "grant" => await GrantQuietAsync(communityId, change.RoleId, change.UserId),
                "revoke_grant" => await RevokeQuietAsync(communityId, change.RoleId, change.UserId),
                "create_role" => await CreateQuietAsync(communityId, change.Name),
                "rename_role" => await RenameQuietAsync(communityId, change.RoleId, change.Name),
                "delete_role" => await DeleteQuietAsync(communityId, change.RoleId),
                "remove_member" => await ExcludeQuietAsync(communityId, change.UserId),
                _ => false
            };
        }
        catch (PostgresException) { return false; }
    }

    private async Task<bool> WritePowerAsync(Guid communityId, Guid roleId, string power, bool enabled)
    {
        if (!GroupChanges.KnownPower(power)) return false;
        if (!await ExistsAsync($"SELECT role_id FROM {Msg}.group_roles WHERE role_id=@p0 AND community_id=@p1", roleId, communityId))
            return false;
        if (enabled)
            await ExecuteAsync($"""
                INSERT INTO {Msg}.group_role_powers(role_id,power) VALUES(@p0,@p1) ON CONFLICT DO NOTHING
                """, roleId, power);
        else
            await ExecuteAsync($"DELETE FROM {Msg}.group_role_powers WHERE role_id=@p0 AND power=@p1", roleId, power);
        return true;
    }

    private async Task<bool> GrantQuietAsync(Guid communityId, Guid roleId, Guid userId)
    {
        if (!await ExistsAsync($"SELECT role_id FROM {Msg}.group_roles WHERE role_id=@p0 AND community_id=@p1", roleId, communityId)) return false;
        if (!await ExistsAsync($"SELECT user_id FROM {Schema}.memberships WHERE community_id=@p0 AND user_id=@p1 AND status='active'", communityId, userId)) return false;
        var held = await ScalarAsync($"""
            SELECT count(*)::int FROM {Msg}.group_role_grants g
            JOIN {Msg}.group_roles r ON r.role_id=g.role_id
            WHERE r.community_id=@p0 AND g.user_id=@p1 AND g.role_id<>@p2
            """, communityId, userId, roleId);
        if (held >= 3) return false;
        await ExecuteAsync($"INSERT INTO {Msg}.group_role_grants(role_id,user_id) VALUES(@p0,@p1) ON CONFLICT DO NOTHING", roleId, userId);
        return true;
    }

    private async Task<bool> RevokeQuietAsync(Guid communityId, Guid roleId, Guid userId)
    {
        if (!await ExistsAsync($"SELECT role_id FROM {Msg}.group_roles WHERE role_id=@p0 AND community_id=@p1", roleId, communityId)) return false;
        await ExecuteAsync($"""
            DELETE FROM {Msg}.group_role_grants g USING {Msg}.group_roles r
            WHERE g.role_id=r.role_id AND g.role_id=@p0 AND g.user_id=@p1 AND r.community_id=@p2
            """, roleId, userId, communityId);
        return true;
    }

    private async Task<bool> CreateQuietAsync(Guid communityId, string name)
    {
        if (GroupRoleNames.Clean(name) != name) return false;
        if (await ScalarAsync($"SELECT count(*)::int FROM {Msg}.group_roles WHERE community_id=@p0", communityId) >= 12) return false;
        await ExecuteAsync($"INSERT INTO {Msg}.group_roles(role_id,community_id,name,created_at) VALUES(@p0,@p1,@p2,@p3)", Guid.NewGuid(), communityId, name, Now);
        return true;
    }

    private async Task<bool> RenameQuietAsync(Guid communityId, Guid roleId, string name)
    {
        if (GroupRoleNames.Clean(name) != name) return false;
        return await ExecuteCountAsync($"UPDATE {Msg}.group_roles SET name=@p0 WHERE role_id=@p1 AND community_id=@p2", name, roleId, communityId) == 1;
    }

    private async Task<bool> DeleteQuietAsync(Guid communityId, Guid roleId)
        => await ExecuteCountAsync($"DELETE FROM {Msg}.group_roles WHERE role_id=@p0 AND community_id=@p1", roleId, communityId) == 1;

    private async Task<bool> ExcludeQuietAsync(Guid communityId, Guid userId)
    {
        var updated = await ExecuteCountAsync($"""
            UPDATE {Schema}.memberships SET status='revoked', revoked_at=@p0
            WHERE community_id=@p1 AND user_id=@p2 AND status='active' AND role='member'
            """, Now, communityId, userId);
        if (updated != 1) return false;
        await ExecuteAsync($"""
            DELETE FROM {Msg}.group_role_grants g USING {Msg}.group_roles r
            WHERE g.role_id=r.role_id AND r.community_id=@p0 AND g.user_id=@p1
            """, communityId, userId);
        await ExecuteAsync($"""
            DELETE FROM {Msg}.conversation_members m USING {Msg}.conversations c
            WHERE m.conversation_id=c.conversation_id AND c.kind='group' AND c.community_id=@p0 AND m.user_id=@p1
            """, communityId, userId);
        return true;
    }

    private async Task RequirePowerAsync(Guid communityId, string power)
    {
        if (await RequireMemberAsync(communityId) == "headman" || await HasPowerAsync(communityId, power)) return;
        throw CommunityServiceException.Forbidden();
    }

    private async Task RequireJoinPowerAsync(Guid communityId)
    {
        var role = await RequireMemberAsync(communityId);
        if (role is "headman" or "curator" || await HasPowerAsync(communityId, "joins")) return;
        throw CommunityServiceException.Forbidden();
    }

    private Task<bool> HasPowerAsync(Guid communityId, string power) => ExistsAsync($"""
        SELECT p.power FROM {Msg}.group_role_powers p
        JOIN {Msg}.group_role_grants g ON g.role_id=p.role_id
        JOIN {Msg}.group_roles r ON r.role_id=p.role_id
        WHERE r.community_id=@p0 AND g.user_id=@p1 AND p.power=@p2
        """, communityId, UserId, power);

    private async Task<int> VotesAsync(Guid ballotId, int ordinal) => await ScalarAsync($"""
        SELECT count(*)::int FROM {Msg}.ballot_votes AS vote
        JOIN {Msg}.ballot_options AS choice ON choice.option_id=vote.option_id AND choice.ballot_id=vote.ballot_id
        JOIN {Msg}.ballots AS ballot ON ballot.ballot_id=vote.ballot_id
        JOIN {Schema}.memberships AS member ON member.community_id=ballot.community_id AND member.user_id=vote.user_id AND member.status='active'
        WHERE vote.ballot_id=@p0 AND choice.ordinal=@p1
        """, ballotId, ordinal);

    private async Task<string?> RoleLabelAsync(Guid communityId, Guid roleId)
    {
        await using var command = Command($"SELECT name FROM {Msg}.group_roles WHERE role_id=@p0 AND community_id=@p1", roleId, communityId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? reader.GetString(0) : null;
    }

    private async Task<string?> MemberLabelAsync(Guid communityId, Guid userId, bool ordinaryOnly)
    {
        await using var command = Command($"""
            SELECT coalesce(nullif(u.display_name, ''), u.username), m.role
            FROM {Schema}.memberships m
            JOIN {configuration.Accounts.QuotedSchema}.users u ON u.user_id=m.user_id
            WHERE m.community_id=@p0 AND m.user_id=@p1 AND m.status='active'
            """, communityId, userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        if (ordinaryOnly && reader.GetString(1) != "member") return null;
        return reader.GetString(0);
    }
}
