using Zapara.Contracts.Communities;
namespace Zapara.Server.Communities;
internal sealed partial class CommunityRepository
{
    private async Task RequireRoleAuthorityAsync(Guid communityId, Guid roleId, string permission, string? addedPower = null, int? newPosition = null, Guid? actingUser = null)
    {
        await RequireMemberAsync(communityId);
        var actor = await SpaceActorAsync(communityId,actingUser??UserId);
        if(!actor.Powers.Contains(permission)) throw CommunityServiceException.Forbidden();
        int position;
        await using(var command=Command($"SELECT position FROM {Msg}.group_roles WHERE community_id=@p0 AND role_id=@p1",communityId,roleId))
            position=await command.ExecuteScalarAsync(ct) is int n?n:throw CommunityServiceException.NotFound();
        if(actor.OfficialRole=="headman") return;
        var powers=new List<string>();
        await using(var command=Command($"SELECT power FROM {Msg}.group_role_powers WHERE role_id=@p0",roleId))
        await using(var reader=await command.ExecuteReaderAsync(ct))
            while(await reader.ReadAsync(ct)) powers.Add(reader.GetString(0));
        if(addedPower is not null) powers.Add(addedPower);
        if(actor.Roles.Contains(roleId) || !GroupPermissionRules.CanDelegate(false,actor.Position,Math.Max(position,newPosition??position),actor.Powers,powers)) throw CommunityServiceException.Forbidden();
        // Assignment must not smuggle a channel permission which the delegator does not hold.
        foreach(var topic in await SpaceTopicsAsync(communityId))
        {
            var allows=(await AccessRulesAsync(topic.Id)).Where(r=>r.RoleId==roleId && r.State=="allow").Select(r=>r.Power).ToArray();
            if(allows.Length>0 && allows.Except(await TopicPermissionsAsync(communityId,topic,actor)).Any()) throw CommunityServiceException.Forbidden();
        }
    }
    private async Task RequireMemberAuthorityAsync(Guid communityId,Guid userId,string permission,Guid? actingUser=null)
    {
        var actor=await SpaceActorAsync(communityId,actingUser??UserId);
        var target=await SpaceActorAsync(communityId,userId);
        if(!actor.Powers.Contains(permission)) throw CommunityServiceException.Forbidden();
        if(actor.OfficialRole=="headman") return;
        if(userId==(actingUser??UserId) || target.OfficialRole is "headman" or "curator" || target.Position>=actor.Position) throw CommunityServiceException.Forbidden();
    }
    internal async Task<GroupDeskResponse> SaveRoleSettingsAsync(Guid communityId,Guid roleId,GroupRoleSettingsRequest request)
    {
        if(request is null || GroupRoleNames.Clean(request.Name) is not string name || GroupTopicNames.Icon(request.Icon) is not string icon || request.Position is <0 or >10000) throw CommunityServiceException.InvalidRequest();
        await RequireRoleAuthorityAsync(communityId,roleId,"roles",newPosition:request.Position);
        if(await ExecuteCountAsync($"UPDATE {Msg}.group_roles SET name=@p0,icon=@p1,position=@p2,revision=revision+1 WHERE community_id=@p3 AND role_id=@p4 AND revision=@p5",name,icon,request.Position,communityId,roleId,request.ExpectedRevision)!=1) throw CommunityServiceException.Conflict("revision_conflict");
        await ManagementAuditAsync(communityId,"role.updated",roleId);
        return await DeskAsync(communityId);
    }
    internal async Task<GroupRoleImpactResponse> RoleImpactAsync(Guid communityId,Guid roleId)
    {
        await RequireRoleAuthorityAsync(communityId,roleId,"roles");
        return new(roleId,await ScalarAsync($"SELECT count(*)::int FROM {Msg}.group_role_grants WHERE role_id=@p0",roleId),await ScalarAsync($"SELECT count(*)::int FROM {Msg}.group_topic_access WHERE role_id=@p0",roleId));
    }
    private async Task RequireChangeAuthorityAsync(Guid communityId,GroupChange change,Guid? actingUser=null)
    {
        var permission=change.Kind switch { "grant" or "revoke_grant"=>"grants", "remove_member"=>"exclude", _=>"roles" };
        var actor=await SpaceActorAsync(communityId,actingUser??UserId);
        if(!actor.Powers.Contains(permission)) throw CommunityServiceException.Forbidden();
        if(change.Kind=="create_role" && actor.OfficialRole!="headman" && actor.Position<=0) throw CommunityServiceException.Forbidden();
        if(change.RoleId!=Guid.Empty) await RequireRoleAuthorityAsync(communityId,change.RoleId,permission,change.Kind=="power"&&change.Enabled?change.Power:null,actingUser:actingUser);
        if(change.UserId!=Guid.Empty) await RequireMemberAuthorityAsync(communityId,change.UserId,permission,actingUser);
    }
}
