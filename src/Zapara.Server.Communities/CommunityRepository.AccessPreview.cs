using Zapara.Contracts.Communities;

namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository
{
    private sealed record ValidatedAccessChange(SpaceTopic Topic, IReadOnlyList<GroupAccessRule> ExistingRules);

    // This validation path deliberately avoids RequireMemberAsync: that method can
    // initialize starter topics. A preview must never insert even an initialization receipt.
    private async Task<ValidatedAccessChange> ValidateAccessChangeAsync(Guid communityId, Guid topicId, GroupTopicAccessRequest request)
    {
        await LockCommunityAsync(communityId);
        if (await ActiveRoleAsync(communityId) is null) throw CommunityServiceException.Forbidden();
        var actor = await SpaceActorAsync(communityId, UserId);
        var topic = await SpaceTopicAsync(communityId, topicId);
        var existing = await AccessRulesAsync(topicId);
        var current = ResolveTopic(topic, actor, existing);
        if (!current.Permissions.Contains("read")) throw CommunityServiceException.NotFound();
        if (!current.Permissions.Contains("access")) throw CommunityServiceException.Forbidden();
        if (request is null) throw CommunityServiceException.InvalidRequest();
        if (request.ExpectedRevision != topic.Revision) throw CommunityServiceException.Conflict("revision_conflict");
        await RequireFreshAccessSnapshotAsync(communityId, topic);
        await ValidateTopicAccessRulesAsync(communityId, topic, actor, existing, request.Rules);
        return new(topic, existing);
    }

    // Shared by Create initial ACL and existing-topic preview/save. The caller owns
    // the creation/global or existing-topic/effective access gate and transaction.
    private async Task ValidateTopicAccessRulesAsync(Guid communityId, SpaceTopic topic, SpaceActor actor,
        IReadOnlyList<GroupAccessRule> existing, IReadOnlyList<GroupAccessRule>? rules)
    {
        if (rules is null || rules.Count > 240 || rules.Any(r => r is null)
            || rules.Select(r => (r.RoleId, r.Power)).Distinct().Count() != rules.Count)
            throw CommunityServiceException.InvalidRequest();
        var restored = topic with { Archived = false };
        var grantable = actor.OfficialRole == "headman" ? GroupPermissionRules.Powers : ResolveTopic(restored, actor, existing).Permissions;
        foreach (var rule in rules)
        {
            if (!GroupChanges.KnownPower(rule.Power) || rule.State is not ("allow" or "deny" or "inherit") || rule.RoleId == Guid.Empty)
                throw CommunityServiceException.InvalidRequest();
            if (rule.RoleId is Guid role && !await ExistsAsync($"SELECT 1 FROM {Msg}.group_roles WHERE community_id=@p0 AND role_id=@p1", communityId, role))
                throw CommunityServiceException.InvalidRequest();
            if (GroupPermissionRules.GroupOnlyPowers.Contains(rule.Power))
            {
                // Existing inert entries may round-trip once and are removed by Save.
                if (rule.State != "inherit" && !existing.Contains(rule)) throw CommunityServiceException.InvalidRequest();
                continue;
            }
            if (rule.State == "allow" && !existing.Contains(rule) && !grantable.Contains(rule.Power)) throw CommunityServiceException.Forbidden();
        }
        if (actor.OfficialRole != "headman")
        {
            var before = ResolveTopic(restored, actor, existing).Permissions;
            var after = ResolveTopic(restored, actor, rules).Permissions;
            if (after.Except(before).Any()) throw CommunityServiceException.Forbidden();
            if (existing.Where(r => !GroupPermissionRules.GroupOnlyPowers.Contains(r.Power) && !grantable.Contains(r.Power)).Any(r => !rules.Contains(r)))
                throw CommunityServiceException.Forbidden();
        }
    }

    private static GroupPermissionRules.Resolution ResolveTopic(SpaceTopic topic, SpaceActor actor, IEnumerable<GroupAccessRule> rules)
        => GroupPermissionRules.Resolve(true, actor.OfficialRole == "headman", actor.OfficialRole == "curator", actor.Powers,
            actor.Roles, rules, topic.Kind, topic.Template, topic.Archived, topic.WritePolicy);

    internal async Task<GroupTopicAccessPreviewResponse> TopicAccessPreviewAsync(Guid communityId, Guid topicId, GroupTopicAccessRequest request)
    {
        var change = await ValidateAccessChangeAsync(communityId, topicId, request);
        var users = new List<Guid>();
        await using (var command = Command($"SELECT user_id FROM {Schema}.memberships WHERE community_id=@p0 AND status='active' ORDER BY user_id", communityId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) users.Add(reader.GetGuid(0));
        var participants = new List<GroupTopicAccessPreviewParticipantResponse>(users.Count);
        foreach (var userId in users)
        {
            var actor = await SpaceActorAsync(communityId, userId);
            var before = ResolveTopic(change.Topic, actor, change.ExistingRules);
            var after = ResolveTopic(change.Topic, actor, request.Rules);
            participants.Add(new(userId, before.Permissions, after.Permissions, after.Sources));
        }
        return new(topicId, change.Topic.Revision,
            participants.Count(p => !p.BeforePermissions.SequenceEqual(p.AfterPermissions)),
            participants.Where(p => p.BeforePermissions.Contains("read")).Select(p => p.UserId).ToArray(),
            participants.Where(p => p.AfterPermissions.Contains("read")).Select(p => p.UserId).ToArray(), participants);
    }
}
