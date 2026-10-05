using Npgsql;
using Zapara.Contracts.Communities;

namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository
{
    // Reads for the built-in general thread. This id is not a row in group_topics.
    private static readonly Guid GeneralRead = new("00000000-0000-0000-0000-000000000001");

    internal Task<GroupTopicListResponse> TopicsAsync(Guid communityId, bool includeTyped = false)
        => SpaceTopicListAsync(communityId, includeTyped);

    internal async Task<GroupTopicListResponse> CreateTopicAsync(Guid communityId, GroupTopicRequest request, bool includeTyped = false)
    {
        await RequirePowerAsync(communityId, "channels");
        if (request is null) throw CommunityServiceException.InvalidRequest();
        var title = GroupTopicNames.Title(request.Title) ?? throw CommunityServiceException.InvalidRequest();
        var icon = GroupTopicNames.Icon(request.Icon) ?? throw CommunityServiceException.InvalidRequest();
        var kind = GroupTopicNames.Kind(request.Kind) ?? throw CommunityServiceException.InvalidRequest();
        await ValidateTopicMetadataAsync(communityId, request, kind);
        var id = Guid.NewGuid();
        var description = GroupTopicNames.Description(request.Description) ?? throw CommunityServiceException.InvalidRequest();
        var accent = GroupTopicNames.Accent(request.Accent) ?? throw CommunityServiceException.InvalidRequest();
        var pinned = request.Pinned ?? false;
        var writePolicy = GroupTopicNames.WritePolicy(request.WritePolicy) ?? throw CommunityServiceException.InvalidRequest();
        if (await ScalarAsync($"SELECT count(*)::int FROM {Msg}.group_topics WHERE community_id=@p0", communityId) >= 24)
            throw CommunityServiceException.InvalidRequest();
        try
        {
            var accessSnapshot = await CurrentAccessSnapshotAsync(communityId);
            await ExecuteAsync($"""
                INSERT INTO {Msg}.group_topics(topic_id,community_id,title,icon,kind,description,accent,pinned,write_policy,created_by,created_at,access_snapshot)
                VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11)
                """, id, communityId, title, icon, kind, description, accent, pinned, writePolicy, UserId, Now, accessSnapshot);
            await SaveTopicMetadataAsync(communityId,id,request,kind);
            await ApplyInitialTopicAccessAsync(communityId, id, request.InitialAccessRules);
            await ManagementAuditAsync(communityId,"topic.created",id);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw CommunityServiceException.Conflict("revision_conflict"); }
        return await TopicsAsync(communityId, includeTyped);
    }

    private async Task ApplyInitialTopicAccessAsync(Guid communityId, Guid topicId, IReadOnlyList<GroupAccessRule>? rules)
    {
        if (rules is null || rules.Count == 0) return;
        // An existing topic's local access grant cannot authorize policy for a new one.
        // Pure inheritance is the unchanged default and needs no extra capability.
        if (rules.Any(rule => rule is null || rule.State != "inherit"))
            await RequirePowerAsync(communityId, "access");
        var actor = await SpaceActorAsync(communityId, UserId);
        var topic = await SpaceTopicAsync(communityId, topicId);
        await ValidateTopicAccessRulesAsync(communityId, topic, actor, Array.Empty<GroupAccessRule>(), rules);
        foreach (var rule in rules.Where(rule => rule.State != "inherit" && !GroupPermissionRules.GroupOnlyPowers.Contains(rule.Power)))
            await ExecuteAsync($"INSERT INTO {Msg}.group_topic_access(topic_id,role_id,power,state) VALUES(@p0,@p1,@p2,@p3)", topicId, rule.RoleId, rule.Power, rule.State);
        // Do not re-authorize read after applying ACL: a delegate may intentionally
        // create for another role and receive a successful, filtered topics list.
    }

    internal async Task<GroupTopicListResponse> RenameTopicAsync(Guid communityId, Guid topicId, GroupTopicRequest request, bool includeTyped = false, bool modernMetadata = true)
    {
        var officialRole = await RequireMemberAsync(communityId);
        if (request is null) throw CommunityServiceException.InvalidRequest();
        if (request.InitialAccessRules is not null) throw CommunityServiceException.InvalidRequest();
        var title = GroupTopicNames.Title(request.Title) ?? throw CommunityServiceException.InvalidRequest();
        var icon = GroupTopicNames.Icon(request.Icon) ?? throw CommunityServiceException.InvalidRequest();
        var requestedKind = GroupTopicNames.Kind(request.Kind) ?? throw CommunityServiceException.InvalidRequest();
        await RequireTopicPermissionAsync(communityId,topicId,"read");
        var fullTopic = await SpaceTopicAsync(communityId,topicId);
        if (!modernMetadata)
            request = new(request.Title, request.Icon, request.Kind, request.Description, request.Accent, request.Pinned, request.WritePolicy,
                fullTopic.Template, fullTopic.CategoryId, fullTopic.Position, fullTopic.Subject, request.ExpectedRevision);
        if(request.Template is null) request = new(request.Title,request.Icon,request.Kind,request.Description,request.Accent,request.Pinned,request.WritePolicy,fullTopic.Template,request.CategoryId,request.Position,request.Subject??fullTopic.Subject,request.ExpectedRevision);
        await ValidateTopicMetadataAsync(communityId,request,requestedKind);
        if(request.Template != fullTopic.Template) await RequireTopicPermissionAsync(communityId,topicId,"access");
        if(request.ExpectedRevision is long rev && rev != fullTopic.Revision) throw CommunityServiceException.Conflict("revision_conflict");
        var existing = await TopicSettingsAsync(communityId, topicId) ?? throw CommunityServiceException.NotFound();
        if (requestedKind != existing.Kind) throw CommunityServiceException.InvalidRequest();
        var description = request.Description is null ? existing.Description
            : GroupTopicNames.Description(request.Description) ?? throw CommunityServiceException.InvalidRequest();
        var accent = request.Accent is null ? existing.Accent
            : GroupTopicNames.Accent(request.Accent) ?? throw CommunityServiceException.InvalidRequest();
        var pinned = request.Pinned ?? existing.Pinned;
        if(pinned != existing.Pinned) await RequireTopicPermissionAsync(communityId,topicId,"pin");
        if(request.WritePolicy is not null && request.WritePolicy != existing.WritePolicy) await RequireTopicPermissionAsync(communityId,topicId,"access");
        var writePolicy = request.WritePolicy is null ? existing.WritePolicy
            : GroupTopicNames.WritePolicy(request.WritePolicy) ?? throw CommunityServiceException.InvalidRequest();
        if (officialRole != "headman" && (request.Template != fullTopic.Template || writePolicy != fullTopic.WritePolicy))
        {
            var actor = await SpaceActorAsync(communityId, UserId);
            var rules = await AccessRulesAsync(topicId);
            // Compare the restored state too: archiving must not hide a privilege increase.
            var currentRights = GroupPermissionRules.Calculate(true, false, officialRole == "curator", actor.Powers, actor.Roles, rules,
                fullTopic.Kind, fullTopic.Template, false, fullTopic.WritePolicy);
            var proposedRights = GroupPermissionRules.Calculate(true, false, officialRole == "curator", actor.Powers, actor.Roles, rules,
                fullTopic.Kind, request.Template!, false, writePolicy);
            if (proposedRights.Except(currentRights).Any()) throw CommunityServiceException.Forbidden();
        }
        if (!(await TopicPermissionsAsync(communityId, fullTopic)).Contains("channels"))
        {
            await RequireTopicPermissionAsync(communityId, topicId, "pin");
            if (title != fullTopic.Title || icon != fullTopic.Icon || description != fullTopic.Description || accent != fullTopic.Accent
                || writePolicy != fullTopic.WritePolicy || request.Template != fullTopic.Template || request.CategoryId != fullTopic.CategoryId
                || request.Position != fullTopic.Position || request.Subject != fullTopic.Subject) throw CommunityServiceException.Forbidden();
        }
        try
        {
            var updated = await ExecuteCountAsync($"""
                UPDATE {Msg}.group_topics
                SET title=@p0, icon=@p1, description=@p2, accent=@p3, pinned=@p4, write_policy=@p5, revision=revision+1
                WHERE topic_id=@p6 AND community_id=@p7
                """, title, icon, description, accent, pinned, writePolicy, topicId, communityId);
            if (updated != 1) throw CommunityServiceException.NotFound();
            await SaveTopicMetadataAsync(communityId,topicId,request,requestedKind);
            await ManagementAuditAsync(communityId,"topic.updated",topicId);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw CommunityServiceException.Conflict("revision_conflict"); }
        return await TopicsAsync(communityId, includeTyped);
    }

    internal async Task<(GroupTopicListResponse Page, IReadOnlyList<Guid> MessageIds)> DeleteTopicAsync(Guid communityId, Guid topicId, bool includeTyped = false)
    {
        await RequireTopicPermissionAsync(communityId,topicId,"channels");
        if (await TopicKindAsync(communityId, topicId) is null) throw CommunityServiceException.NotFound();
        if(await ExistsAsync($"SELECT 1 FROM {Msg}.group_topic_access WHERE topic_id=@p0",topicId) || await ExistsAsync($"SELECT 1 FROM {Msg}.group_homework_details WHERE topic_id=@p0",topicId)) throw CommunityServiceException.Conflict("revision_conflict");
        var conversationId = await EnsureGroupConversationAsync(communityId);
        var removed = new List<Guid>();
        await using (var command = Command($"SELECT message_id FROM {Msg}.chat_messages WHERE conversation_id=@p0 AND topic_id=@p1", conversationId, topicId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) removed.Add(reader.GetGuid(0));
        await ExecuteAsync($"DELETE FROM {Msg}.chat_messages WHERE conversation_id=@p0 AND topic_id=@p1", conversationId, topicId);
        await ExecuteAsync($"DELETE FROM {Msg}.group_topic_reads WHERE community_id=@p0 AND topic_id=@p1", communityId, topicId);
        await ExecuteAsync($"DELETE FROM {Msg}.group_topics WHERE topic_id=@p0 AND community_id=@p1", topicId, communityId);
        await ManagementAuditAsync(communityId,"topic.deleted",topicId);
        return (await TopicsAsync(communityId, includeTyped), removed);
    }

    internal async Task<ChatMessageResponse> SendTopicMessageAsync(Guid conversationId, string body, Guid? topicId, Guid? replyTo = null)
    {
        body = CommunityValidation.Message(body);
        await RequireConversationAsync(conversationId);
        var info = await ConversationInfoAsync(conversationId);
        if (info.Kind != "group") throw CommunityServiceException.InvalidRequest();
        if (topicId is Guid selected) await RequireWritableChatTopicAsync(info.CommunityId, selected);
        if (replyTo is Guid parent && !await ReplyMatchesTopicAsync(conversationId, parent, topicId))
            throw CommunityServiceException.InvalidRequest();
        var id = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO {Msg}.chat_messages(message_id,conversation_id,sender_id,body,created_at,topic_id,reply_to)
            VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)
            """, id, conversationId, UserId, body, Now, topicId, replyTo);
        return new(id, conversationId, UserId, await DisplayNameAsync(UserId), body, Now, replyTo: replyTo);
    }

    private async Task<GroupTopicResponse> DescribeTopicAsync(Guid communityId, Guid conversationId, Guid? topicId, string title, string icon, string description, string accent, bool pinned, string writePolicy, bool canManage)
    {
        var readKey = topicId ?? GeneralRead;
        string? lastBody = null;
        string? lastAuthor = null;
        DateTimeOffset? lastAt = null;
        var previewSql = topicId is null
            ? $"""
            SELECT CASE WHEN m.deleted THEN 'Сообщение удалено' ELSE m.body END,
                   coalesce(u.display_name, u.username), m.created_at
            FROM {Msg}.chat_messages m
            JOIN {configuration.Accounts.QuotedSchema}.users u ON u.user_id=m.sender_id
            WHERE m.conversation_id=@p0 AND m.topic_id IS NULL
            ORDER BY m.message_no DESC LIMIT 1
            """
            : $"""
            SELECT CASE WHEN m.deleted THEN 'Сообщение удалено' ELSE m.body END,
                   coalesce(u.display_name, u.username), m.created_at
            FROM {Msg}.chat_messages m
            JOIN {configuration.Accounts.QuotedSchema}.users u ON u.user_id=m.sender_id
            WHERE m.conversation_id=@p0 AND m.topic_id=@p1
            ORDER BY m.message_no DESC LIMIT 1
            """;
        await using (var command = topicId is null ? Command(previewSql, conversationId) : Command(previewSql, conversationId, topicId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            if (await reader.ReadAsync(ct))
            {
                lastBody = reader.GetString(0);
                lastAuthor = reader.GetString(1);
                lastAt = AsUtc(reader.GetFieldValue<DateTimeOffset>(2));
            }
        var unreadSql = topicId is null
            ? $"""
            SELECT count(*)::int FROM {Msg}.chat_messages m
            WHERE m.conversation_id=@p0 AND m.topic_id IS NULL AND m.sender_id<>@p1
              AND m.message_no > COALESCE((SELECT last_read_no FROM {Msg}.group_topic_reads r WHERE r.community_id=@p2 AND r.topic_id=@p3 AND r.user_id=@p1), 0)
            """
            : $"""
            SELECT count(*)::int FROM {Msg}.chat_messages m
            WHERE m.conversation_id=@p0 AND m.topic_id=@p4 AND m.sender_id<>@p1
              AND m.message_no > COALESCE((SELECT last_read_no FROM {Msg}.group_topic_reads r WHERE r.community_id=@p2 AND r.topic_id=@p3 AND r.user_id=@p1), 0)
            """;
        var unread = topicId is null
            ? await ScalarAsync(unreadSql, conversationId, UserId, communityId, readKey)
            : await ScalarAsync(unreadSql, conversationId, UserId, communityId, readKey, topicId);
        return new(topicId, title, icon, lastBody, lastAuthor, lastAt, unread, canManage && topicId is not null, GroupTopicNames.ChatKind, 0, description, accent, pinned, writePolicy, writePolicy == GroupTopicNames.AllWriters || canManage);
    }


    private async Task<GroupTopicResponse> DescribeBallotTopicAsync(Guid communityId, Guid topicId, string title, string icon, string description, string accent, bool pinned, string writePolicy, bool canManage)
    {
        string? lastBody = null;
        string? lastAuthor = null;
        DateTimeOffset? lastAt = null;
        await using (var command = Command($"""
            SELECT ballot.question, coalesce(author.display_name, author.username), ballot.created_at
            FROM {Msg}.ballots ballot
            LEFT JOIN {configuration.Accounts.QuotedSchema}.users author ON author.user_id=ballot.created_by
            WHERE ballot.community_id=@p0 AND ballot.topic_id=@p1
            ORDER BY ballot.created_at DESC, ballot.ballot_id DESC LIMIT 1
            """, communityId, topicId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            if (await reader.ReadAsync(ct))
            {
                lastBody = reader.GetString(0);
                lastAuthor = reader.IsDBNull(1) ? null : reader.GetString(1);
                lastAt = AsUtc(reader.GetFieldValue<DateTimeOffset>(2));
            }
        var active = await ScalarAsync($"""
            SELECT count(*)::int FROM {Msg}.ballots
            WHERE community_id=@p0 AND topic_id=@p1 AND status IN ('collecting','open') AND deadline_at>@p2
            """, communityId, topicId, Now);
        return new(topicId, title, icon, lastBody, lastAuthor, lastAt, 0, canManage, GroupTopicNames.BallotsKind, active, description, accent, pinned, writePolicy, writePolicy == GroupTopicNames.AllWriters || canManage);
    }

    private async Task<(string Kind, string Description, string Accent, bool Pinned, string WritePolicy)?> TopicSettingsAsync(Guid communityId, Guid topicId)
    {
        await using var command = Command($"""
            SELECT kind, description, accent, pinned, write_policy
            FROM {Msg}.group_topics WHERE topic_id=@p0 AND community_id=@p1
            """, topicId, communityId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetString(4));
    }

    private async Task<string?> TopicKindAsync(Guid communityId, Guid topicId)
        => (await TopicSettingsAsync(communityId, topicId))?.Kind;

    private async Task RequireChatTopicAsync(Guid communityId, Guid topicId)
    {
        await RequireTopicPermissionAsync(communityId,topicId,"read");
        var settings = await TopicSettingsAsync(communityId, topicId) ?? throw CommunityServiceException.NotFound();
        if (settings.Kind is not (GroupTopicNames.ChatKind or "materials")) throw CommunityServiceException.InvalidRequest();
    }

    private async Task RequireWritableChatTopicAsync(Guid communityId, Guid topicId)
    {
        await RequireTopicPermissionAsync(communityId,topicId,"post");
        var settings = await TopicSettingsAsync(communityId, topicId) ?? throw CommunityServiceException.NotFound();
        if (settings.Kind is not (GroupTopicNames.ChatKind or "materials")) throw CommunityServiceException.InvalidRequest();

    }

    private async Task ValidateBallotTopicAsync(Guid communityId, Guid? topicId)
    {
        if (topicId is not Guid selected) return;
        await RequireTopicPermissionAsync(communityId,selected,"read");
        var settings = await TopicSettingsAsync(communityId, selected) ?? throw CommunityServiceException.NotFound();
        if (settings.Kind != GroupTopicNames.BallotsKind) throw CommunityServiceException.InvalidRequest();
    }

    private async Task RequireBallotPublishTopicAsync(Guid communityId, Guid? topicId)
    {
        if (topicId is not Guid selected) return;
        await RequireTopicPermissionAsync(communityId,selected,"read");
        var settings = await TopicSettingsAsync(communityId, selected) ?? throw CommunityServiceException.NotFound();
        if (settings.Kind != GroupTopicNames.BallotsKind) throw CommunityServiceException.InvalidRequest();
        await RequireTopicPermissionAsync(communityId,selected,"ballots");

    }

    private Task<bool> ReplyMatchesTopicAsync(Guid conversationId, Guid replyTo, Guid? topicId) => topicId is Guid selected
        ? ExistsAsync($"SELECT message_id FROM {Msg}.chat_messages WHERE conversation_id=@p0 AND message_id=@p1 AND topic_id=@p2", conversationId, replyTo, selected)
        : ExistsAsync($"SELECT message_id FROM {Msg}.chat_messages WHERE conversation_id=@p0 AND message_id=@p1 AND topic_id IS NULL", conversationId, replyTo);

    private async Task<(string Kind, Guid CommunityId)> ConversationInfoAsync(Guid conversationId)
    {
        await using var command = Command($"SELECT kind, community_id FROM {Msg}.conversations WHERE conversation_id=@p0", conversationId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw CommunityServiceException.NotFound();
        return (reader.GetString(0), reader.GetGuid(1));
    }

    private async Task MarkTopicReadAsync(Guid communityId, Guid conversationId, Guid? topicId, Guid throughMessageId)
    {
        var readKey = topicId ?? GeneralRead;
        var maxSql = $"SELECT message_no FROM {Msg}.chat_messages WHERE conversation_id=@p0 AND message_id=@p1 AND " +
            (topicId is null ? "topic_id IS NULL" : "topic_id=@p2");
        long max;
        await using (var command = topicId is null ? Command(maxSql, conversationId, throughMessageId) : Command(maxSql, conversationId, throughMessageId, topicId))
        {
            var value = await command.ExecuteScalarAsync(ct);
            max = value is long number ? number : throw CommunityServiceException.InvalidRequest();
        }
        await ExecuteAsync($"""
            INSERT INTO {Msg}.group_topic_reads(community_id,topic_id,user_id,last_read_no)
            VALUES(@p0,@p1,@p2,@p3)
            ON CONFLICT (community_id, topic_id, user_id) DO UPDATE
            SET last_read_no = GREATEST({Msg}.group_topic_reads.last_read_no, EXCLUDED.last_read_no)
            """, communityId, readKey, UserId, max);
    }
}
