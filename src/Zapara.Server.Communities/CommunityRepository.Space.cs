using System.Text.Json;
using Zapara.Contracts.Communities;
namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository
{
    private static string? PreviewLine(string? text)
    {
        if (text is null) return null;
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= 160 ? line : line[..160] + "…";
    }
    private sealed record SpaceTopic(Guid Id, string Title, string Icon, string Kind, string Description, string Accent,
        bool Pinned, string WritePolicy, string Template, Guid? CategoryId, int Position, string? Subject, bool Archived, long Revision, byte[]? AccessSnapshot);
    private sealed record SpaceActor(string OfficialRole, Guid[] Roles, string[] Powers, int Position);

    private async Task<SpaceActor> SpaceActorAsync(Guid communityId, Guid userId, Guid? previewRole = null)
    {
        string? official;
        await using (var cmd = Command($"SELECT role FROM {Schema}.memberships WHERE community_id=@p0 AND user_id=@p1 AND status='active'", communityId, userId))
            official = await cmd.ExecuteScalarAsync(ct) as string;
        if (official is null) throw CommunityServiceException.NotFound();
        var roles = new List<Guid>(); var position = -1;
        await using (var cmd = previewRole is null
            ? Command($"SELECT r.role_id,r.position FROM {Msg}.group_roles r JOIN {Msg}.group_role_grants g ON g.role_id=r.role_id WHERE r.community_id=@p0 AND g.user_id=@p1", communityId, userId)
            : Command($"SELECT role_id,position FROM {Msg}.group_roles WHERE community_id=@p0 AND role_id=@p1", communityId, previewRole))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) { roles.Add(reader.GetGuid(0)); position = Math.Max(position, reader.GetInt32(1)); }
        if (previewRole is not null && roles.Count == 0) throw CommunityServiceException.NotFound();
        var powers = new HashSet<string>(["read", "post", "media", "vote", "formsRespond"]);
        await using (var cmd = Command($"SELECT power FROM {Msg}.group_role_powers WHERE role_id=ANY(@p0)", roles.ToArray()))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) powers.Add(reader.GetString(0));
        if (previewRole is not null) official = "member";
        if (official == "headman") powers.UnionWith(GroupPermissionRules.Powers);
        if (official == "curator") powers.Add("joins");
        return new(official, roles.ToArray(), powers.ToArray(), position);
    }

    private async Task<List<SpaceTopic>> SpaceTopicsAsync(Guid communityId)
    {
        var list = new List<SpaceTopic>();
        await using var cmd = Command($"SELECT topic_id,title,icon,kind,description,accent,pinned,write_policy,template,category_id,position,subject,archived,revision,access_snapshot FROM {Msg}.group_topics WHERE community_id=@p0 ORDER BY position,title,topic_id", communityId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetBoolean(6),r.GetString(7),r.GetString(8),r.IsDBNull(9)?null:r.GetGuid(9),r.GetInt32(10),r.IsDBNull(11)?null:r.GetString(11),r.GetBoolean(12),r.GetInt64(13),r.IsDBNull(14)?null:(byte[])r.GetValue(14)));
        return list;
    }
    private async Task<SpaceTopic> SpaceTopicAsync(Guid communityId, Guid topicId)
        => (await SpaceTopicsAsync(communityId)).SingleOrDefault(t => t.Id == topicId) ?? throw CommunityServiceException.NotFound();
    private async Task<List<GroupAccessRule>> AccessRulesAsync(Guid topicId)
    {
        var rules = new List<GroupAccessRule>();
        await using var cmd = Command($"SELECT role_id,power,state FROM {Msg}.group_topic_access WHERE topic_id=@p0 ORDER BY role_id,power", topicId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) rules.Add(new(r.IsDBNull(0)?null:r.GetGuid(0),r.GetString(1),r.GetString(2)));
        return rules;
    }
    private async Task<string[]> TopicPermissionsAsync(Guid communityId, SpaceTopic topic, SpaceActor? actor = null)
    {
        actor ??= await SpaceActorAsync(communityId, UserId);
        return GroupPermissionRules.Calculate(true, actor.OfficialRole == "headman", actor.OfficialRole == "curator", actor.Powers,
            actor.Roles, await AccessRulesAsync(topic.Id), topic.Kind, topic.Template, topic.Archived, topic.WritePolicy);
    }
    private async Task RequireTopicPermissionAsync(Guid communityId, Guid topicId, string permission)
    {
        await RequireMemberAsync(communityId);
        var rights = await TopicPermissionsAsync(communityId, await SpaceTopicAsync(communityId, topicId));
        if (!rights.Contains("read")) throw CommunityServiceException.NotFound();
        if (!rights.Contains(permission)) throw CommunityServiceException.Forbidden();
    }
    private async Task<Guid[]> VisibleTopicIdsAsync(Guid communityId)
    {
        var actor = await SpaceActorAsync(communityId, UserId);
        var ids = new List<Guid>();
        foreach (var topic in await SpaceTopicsAsync(communityId))
            if ((await TopicPermissionsAsync(communityId, topic, actor)).Contains("read")) ids.Add(topic.Id);
        return ids.ToArray();
    }
    private async Task<GroupTopicListResponse> SpaceTopicListAsync(Guid communityId, bool includeTyped, bool archived = false, SpaceActor? preview = null)
    {
        await RequireMemberAsync(communityId);
        await SynchronizeAccessRevisionsAsync(communityId);
        var actor = await SpaceActorAsync(communityId, UserId);
        var effective = preview ?? actor;
        var canManage = effective.Powers.Contains("channels");
        var conversation = await EnsureGroupConversationAsync(communityId);
        var list = new List<GroupTopicResponse>();
        if (!archived)
        {
            var general=await DescribeTopicAsync(communityId,conversation,null,await GeneralTitleAsync(communityId),GroupTopicNames.GeneralIcon,"","default",false,"all",false);
            var permissions=GroupPermissionRules.Calculate(true,effective.OfficialRole=="headman",effective.OfficialRole=="curator",effective.Powers,effective.Roles,[],"chat","chat",false,"all");
            list.Add(new(null,general.Title,general.Icon,PreviewLine(general.LastBody),general.LastAuthor,general.LastAt,general.Unread,false,permissions:permissions));
        }
        foreach (var t in await SpaceTopicsAsync(communityId))
        {
            if (t.Archived != archived || !includeTyped && t.Kind != "chat") continue;
            var rights = await TopicPermissionsAsync(communityId,t,effective);
            if (!rights.Contains("read") || preview is not null && !(await TopicPermissionsAsync(communityId,t,actor)).Contains("read")) continue;
            var old = t.Kind == "ballots"
                ? await DescribeBallotTopicAsync(communityId,t.Id,t.Title,t.Icon,t.Description,t.Accent,t.Pinned,t.WritePolicy,canManage)
                : await DescribeTopicAsync(communityId,conversation,t.Id,t.Title,t.Icon,t.Description,t.Accent,t.Pinned,t.WritePolicy,canManage);
            list.Add(new(t.Id,t.Title,t.Icon,PreviewLine(old.LastBody),old.LastAuthor,old.LastAt,old.Unread,rights.Contains("channels"),t.Kind,old.ActiveBallots,t.Description,t.Accent,t.Pinned,t.WritePolicy,
                rights.Contains("post"),t.Template,t.CategoryId,t.Position,t.Subject,t.Archived,t.Revision,rights,GroupPermissionRules.Supported(t.Kind,t.Template)));
        }
        return new(list.OrderBy(t=>t.TopicId is null?0:1).ThenBy(t=>t.CategoryId).ThenByDescending(t=>t.Pinned).ThenBy(t=>t.Position).ThenBy(t=>t.Title).ToArray(),canManage);
    }
    internal async Task<GroupSpaceResponse> SpaceAsync(Guid communityId)
    {
        var topics = await SpaceTopicListAsync(communityId,true);
        var categories = new List<GroupCategoryResponse>();
        await using (var cmd = Command($"SELECT category_id,title,position,revision FROM {Msg}.group_categories WHERE community_id=@p0 ORDER BY position,title",communityId))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while(await r.ReadAsync(ct)) categories.Add(new(r.GetGuid(0),r.GetString(1),r.GetInt32(2),r.GetInt64(3)));
        if (!topics.CanManageChannels) categories.RemoveAll(c=>!topics.Topics.Any(t=>t.CategoryId==c.CategoryId));
        return new(topics.Topics,categories,GroupPermissionRules.Capabilities,await DeskAsync(communityId));
    }
    internal Task<GroupTopicListResponse> ArchivedTopicsAsync(Guid communityId) => SpaceTopicListAsync(communityId,true,true);
    private Task ManagementAuditAsync(Guid communityId,string action,Guid objectId)
        => ExecuteAsync($"INSERT INTO {Msg}.group_management_audit(event_id,community_id,actor_id,action,object_id,created_at) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",Guid.NewGuid(),communityId,UserId,action,objectId,Now);
    internal async Task<GroupSpaceResponse> SaveCategoryAsync(Guid communityId,GroupCategoryRequest request)
    {
        await RequirePowerAsync(communityId,"channels");
        if (request is null || string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length>40 || request.Title.Any(char.IsControl) || request.Position is < 0 or > 10000) throw CommunityServiceException.InvalidRequest();
        var id=request.CategoryId??Guid.NewGuid();
        if(request.CategoryId is null)
        {
            if(await ScalarAsync($"SELECT count(*)::int FROM {Msg}.group_categories WHERE community_id=@p0",communityId)>=24) throw CommunityServiceException.InvalidRequest();
            await ExecuteAsync($"INSERT INTO {Msg}.group_categories(category_id,community_id,title,position) VALUES(@p0,@p1,@p2,@p3)",id,communityId,request.Title.Trim(),request.Position);
        }
        else if(await ExecuteCountAsync($"UPDATE {Msg}.group_categories SET title=@p0,position=@p1,revision=revision+1 WHERE category_id=@p2 AND community_id=@p3 AND revision=@p4",request.Title.Trim(),request.Position,id,communityId,request.ExpectedRevision)!=1) throw CommunityServiceException.Conflict("revision_conflict");
        await ManagementAuditAsync(communityId,"category.saved",id);
        return await SpaceAsync(communityId);
    }
    internal async Task<GroupSpaceResponse> DeleteCategoryAsync(Guid communityId,Guid categoryId)
    {
        await RequirePowerAsync(communityId,"channels");
        await ExecuteAsync($"UPDATE {Msg}.group_topics SET category_id=NULL,revision=revision+1 WHERE community_id=@p0 AND category_id=@p1",communityId,categoryId);
        if(await ExecuteCountAsync($"DELETE FROM {Msg}.group_categories WHERE category_id=@p0 AND community_id=@p1",categoryId,communityId)!=1) throw CommunityServiceException.NotFound();
        await ManagementAuditAsync(communityId,"category.deleted",categoryId);
        return await SpaceAsync(communityId);
    }
    internal async Task<GroupSpaceResponse> ArchiveTopicAsync(Guid communityId,Guid topicId,GroupArchiveRequest request)
    {
        await RequireTopicPermissionAsync(communityId,topicId,"channels");
        if(request is null || await ExecuteCountAsync($"UPDATE {Msg}.group_topics SET archived=@p0,revision=revision+1 WHERE topic_id=@p1 AND community_id=@p2 AND revision=@p3",request.Archived,topicId,communityId,request.ExpectedRevision)!=1) throw CommunityServiceException.Conflict("revision_conflict");
        await ManagementAuditAsync(communityId,request.Archived?"topic.archived":"topic.restored",topicId);
        return await SpaceAsync(communityId);
    }
    internal async Task<GroupTopicAccessResponse> TopicAccessAsync(Guid communityId,Guid topicId)
    {
        await RequireTopicPermissionAsync(communityId,topicId,"access");
        await SynchronizeAccessRevisionsAsync(communityId);
        var topic=await SpaceTopicAsync(communityId,topicId);
        return new(topicId,topic.Revision,await AccessRulesAsync(topicId));
    }
    internal async Task<GroupSpaceResponse> SetTopicAccessAsync(Guid communityId,Guid topicId,GroupTopicAccessRequest request)
    {
        var validated = await ValidateAccessChangeAsync(communityId, topicId, request);
        if(await AdvanceAccessRevisionAsync(communityId,topicId,request.ExpectedRevision)!=1) throw CommunityServiceException.Conflict("revision_conflict");
        // An account lifecycle transaction can commit while the UPDATE waits for a
        // topic row lock. Recheck in the following READ COMMITTED statement too.
        await RequireFreshAccessSnapshotAsync(communityId, validated.Topic);
        await ExecuteAsync($"DELETE FROM {Msg}.group_topic_access WHERE topic_id=@p0",topicId);
        foreach(var rule in request.Rules.Where(r=>r.State!="inherit" && !GroupPermissionRules.GroupOnlyPowers.Contains(r.Power))) await ExecuteAsync($"INSERT INTO {Msg}.group_topic_access(topic_id,role_id,power,state) VALUES(@p0,@p1,@p2,@p3)",topicId,rule.RoleId,rule.Power,rule.State);
        await ManagementAuditAsync(communityId,"topic.access",topicId);
        return await SpaceAsync(communityId);
    }
    internal async Task<GroupPermissionPreviewResponse> PreviewPermissionsAsync(Guid communityId,GroupPermissionPreviewRequest request)
    {
        await RequirePowerAsync(communityId,"access");
        if(request is null || (request.UserId is null)==(request.RoleId is null)) throw CommunityServiceException.InvalidRequest();
        var actor=await SpaceActorAsync(communityId,request.UserId??UserId,request.RoleId);
        return new((await SpaceTopicListAsync(communityId,true,false,actor)).Topics);
    }
    internal async Task<GroupAuditResponse> GroupAuditAsync(Guid communityId)
    {
        var role = await RequireMemberAsync(communityId);
        if (role != "headman" && !await HasPowerAsync(communityId, "channels")
            && !await HasPowerAsync(communityId, "access") && !await HasPowerAsync(communityId, "roles"))
            throw CommunityServiceException.Forbidden();
        var visible=await VisibleTopicIdsAsync(communityId);
        var list=new List<GroupAuditEventResponse>();
        await using var cmd=Command($"SELECT event_id,actor_id,action,object_id,created_at FROM {Msg}.group_management_audit WHERE community_id=@p0 AND (action NOT LIKE 'topic.%' OR object_id=ANY(@p1)) ORDER BY created_at DESC,event_id DESC LIMIT 100",communityId,visible);
        await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) list.Add(new(r.GetGuid(0),r.IsDBNull(1)?null:r.GetGuid(1),r.GetString(2),r.GetGuid(3),AsUtc(r.GetFieldValue<DateTimeOffset>(4))));
        return new(list);
    }
    private async Task ValidateTopicMetadataAsync(Guid communityId,GroupTopicRequest request,string kind)
    {
        var template=request.Template??(kind=="ballots"?"polls":kind);
        if(!GroupPermissionRules.Supported(kind,template) || request.Position is <0 or >10000 || request.Subject?.Length>200 || request.Subject?.Any(char.IsControl)==true
            || template=="subject" && string.IsNullOrWhiteSpace(request.Subject)) throw CommunityServiceException.InvalidRequest();
        if(request.CategoryId is Guid id && !await ExistsAsync($"SELECT 1 FROM {Msg}.group_categories WHERE community_id=@p0 AND category_id=@p1",communityId,id)) throw CommunityServiceException.InvalidRequest();
    }
    private async Task SaveTopicMetadataAsync(Guid communityId,Guid topicId,GroupTopicRequest request,string kind)
    {
        await ExecuteAsync($"UPDATE {Msg}.group_topics SET template=@p0,category_id=@p1,position=@p2,subject=@p3 WHERE topic_id=@p4 AND community_id=@p5",request.Template??(kind=="ballots"?"polls":kind),request.CategoryId,request.Position,request.Subject,topicId,communityId);
    }
}
