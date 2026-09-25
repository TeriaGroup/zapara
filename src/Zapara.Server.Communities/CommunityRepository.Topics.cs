using Npgsql;
using Zapara.Contracts.Communities;

namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository
{
    // Reads for the built-in general thread. This id is not a row in group_topics.
    private static readonly Guid GeneralRead = new("00000000-0000-0000-0000-000000000001");

    internal async Task<GroupTopicListResponse> TopicsAsync(Guid communityId, bool includeTyped = false)
    {
        var role = await RequireMemberAsync(communityId);
        var conversationId = await EnsureGroupConversationAsync(communityId);
        var canManage = role == "headman" || await HasPowerAsync(communityId, "channels");
        var topics = new List<GroupTopicResponse>
        {
            await DescribeTopicAsync(communityId, conversationId, null, GroupTopicNames.GeneralTitle, GroupTopicNames.GeneralIcon,
                "", GroupTopicNames.DefaultAccent, false, GroupTopicNames.AllWriters, false)
        };
        var rows = new List<(Guid Id, string Title, string Icon, string Kind, string Description, string Accent, bool Pinned, string WritePolicy)>();
        await using (var command = Command($"""
            SELECT topic_id, title, icon, kind, description, accent, pinned, write_policy
            FROM {Msg}.group_topics WHERE community_id=@p0
            """, communityId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetBoolean(6), reader.GetString(7)));
        foreach (var row in rows)
        {
            if (!includeTyped && row.Kind == GroupTopicNames.BallotsKind) continue;
            topics.Add(row.Kind == GroupTopicNames.BallotsKind
                ? await DescribeBallotTopicAsync(communityId, row.Id, row.Title, row.Icon,
                    row.Description, row.Accent, row.Pinned, row.WritePolicy, canManage)
                : await DescribeTopicAsync(communityId, conversationId, row.Id, row.Title, row.Icon,
                    row.Description, row.Accent, row.Pinned, row.WritePolicy, canManage));
        }
        topics.Sort((left, right) =>
        {
            if (left.TopicId is null && right.TopicId is null) return 0;
            if (left.TopicId is null) return -1;
            if (right.TopicId is null) return 1;
            var pinned = right.Pinned.CompareTo(left.Pinned);
            if (pinned != 0) return pinned;
            var activity = Nullable.Compare(right.LastAt, left.LastAt);
            return activity != 0 ? activity : string.Compare(left.Title, right.Title, StringComparison.CurrentCulture);
        });
        return new(topics, canManage);
    }

    internal async Task<GroupTopicListResponse> CreateTopicAsync(Guid communityId, GroupTopicRequest request, bool includeTyped = false)
    {
        await RequirePowerAsync(communityId, "channels");
        if (request is null) throw CommunityServiceException.InvalidRequest();
        var title = GroupTopicNames.Title(request.Title) ?? throw CommunityServiceException.InvalidRequest();
        var icon = GroupTopicNames.Icon(request.Icon) ?? throw CommunityServiceException.InvalidRequest();
        var kind = GroupTopicNames.Kind(request.Kind) ?? throw CommunityServiceException.InvalidRequest();
        var description = GroupTopicNames.Description(request.Description) ?? throw CommunityServiceException.InvalidRequest();
        var accent = GroupTopicNames.Accent(request.Accent) ?? throw CommunityServiceException.InvalidRequest();
        var pinned = request.Pinned ?? false;
        var writePolicy = GroupTopicNames.WritePolicy(request.WritePolicy) ?? throw CommunityServiceException.InvalidRequest();
        if (await ScalarAsync($"SELECT count(*)::int FROM {Msg}.group_topics WHERE community_id=@p0", communityId) >= 24)
            throw CommunityServiceException.InvalidRequest();
        try
        {
            await ExecuteAsync($"""
                INSERT INTO {Msg}.group_topics(topic_id,community_id,title,icon,kind,description,accent,pinned,write_policy,created_by,created_at)
                VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10)
                """, Guid.NewGuid(), communityId, title, icon, kind, description, accent, pinned, writePolicy, UserId, Now);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw CommunityServiceException.Conflict("revision_conflict"); }
        return await TopicsAsync(communityId, includeTyped);
    }

    internal async Task<GroupTopicListResponse> RenameTopicAsync(Guid communityId, Guid topicId, GroupTopicRequest request, bool includeTyped = false)
    {
        await RequirePowerAsync(communityId, "channels");
        if (request is null) throw CommunityServiceException.InvalidRequest();
        var title = GroupTopicNames.Title(request.Title) ?? throw CommunityServiceException.InvalidRequest();
        var icon = GroupTopicNames.Icon(request.Icon) ?? throw CommunityServiceException.InvalidRequest();
        var requestedKind = GroupTopicNames.Kind(request.Kind) ?? throw CommunityServiceException.InvalidRequest();
        var existing = await TopicSettingsAsync(communityId, topicId) ?? throw CommunityServiceException.NotFound();
        if (requestedKind != existing.Kind) throw CommunityServiceException.InvalidRequest();
        var description = request.Description is null ? existing.Description
            : GroupTopicNames.Description(request.Description) ?? throw CommunityServiceException.InvalidRequest();
        var accent = request.Accent is null ? existing.Accent
            : GroupTopicNames.Accent(request.Accent) ?? throw CommunityServiceException.InvalidRequest();
        var pinned = request.Pinned ?? existing.Pinned;
        var writePolicy = request.WritePolicy is null ? existing.WritePolicy
            : GroupTopicNames.WritePolicy(request.WritePolicy) ?? throw CommunityServiceException.InvalidRequest();
        try
        {
            var updated = await ExecuteCountAsync($"""
                UPDATE {Msg}.group_topics
                SET title=@p0, icon=@p1, description=@p2, accent=@p3, pinned=@p4, write_policy=@p5
                WHERE topic_id=@p6 AND community_id=@p7
                """, title, icon, description, accent, pinned, writePolicy, topicId, communityId);
            if (updated != 1) throw CommunityServiceException.NotFound();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw CommunityServiceException.Conflict("revision_conflict"); }
        return await TopicsAsync(communityId, includeTyped);
    }

    internal async Task<(GroupTopicListResponse Page, IReadOnlyList<Guid> MessageIds)> DeleteTopicAsync(Guid communityId, Guid topicId, bool includeTyped = false)
    {
        await RequirePowerAsync(communityId, "channels");
        if (await TopicKindAsync(communityId, topicId) is null) throw CommunityServiceException.NotFound();
        var conversationId = await EnsureGroupConversationAsync(communityId);
        var removed = new List<Guid>();
        await using (var command = Command($"SELECT message_id FROM {Msg}.chat_messages WHERE conversation_id=@p0 AND topic_id=@p1", conversationId, topicId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) removed.Add(reader.GetGuid(0));
        await ExecuteAsync($"DELETE FROM {Msg}.chat_messages WHERE conversation_id=@p0 AND topic_id=@p1", conversationId, topicId);
        await ExecuteAsync($"DELETE FROM {Msg}.group_topic_reads WHERE community_id=@p0 AND topic_id=@p1", communityId, topicId);
        await ExecuteAsync($"DELETE FROM {Msg}.group_topics WHERE topic_id=@p0 AND community_id=@p1", topicId, communityId);
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
        var settings = await TopicSettingsAsync(communityId, topicId) ?? throw CommunityServiceException.NotFound();
        if (settings.Kind != GroupTopicNames.ChatKind) throw CommunityServiceException.InvalidRequest();
    }

    private async Task RequireWritableChatTopicAsync(Guid communityId, Guid topicId)
    {
        var settings = await TopicSettingsAsync(communityId, topicId) ?? throw CommunityServiceException.NotFound();
        if (settings.Kind != GroupTopicNames.ChatKind) throw CommunityServiceException.InvalidRequest();
        if (settings.WritePolicy == GroupTopicNames.ManagersOnly)
            await RequirePowerAsync(communityId, "channels");
    }

    private async Task ValidateBallotTopicAsync(Guid communityId, Guid? topicId)
    {
        if (topicId is not Guid selected) return;
        var settings = await TopicSettingsAsync(communityId, selected) ?? throw CommunityServiceException.NotFound();
        if (settings.Kind != GroupTopicNames.BallotsKind) throw CommunityServiceException.InvalidRequest();
    }

    private async Task RequireBallotPublishTopicAsync(Guid communityId, Guid? topicId)
    {
        if (topicId is not Guid selected) return;
        var settings = await TopicSettingsAsync(communityId, selected) ?? throw CommunityServiceException.NotFound();
        if (settings.Kind != GroupTopicNames.BallotsKind) throw CommunityServiceException.InvalidRequest();
        if (settings.WritePolicy == GroupTopicNames.ManagersOnly)
            await RequirePowerAsync(communityId, "channels");
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

    private async Task MarkTopicReadAsync(Guid communityId, Guid conversationId, Guid? topicId)
    {
        var readKey = topicId ?? GeneralRead;
        var maxSql = topicId is null
            ? $"SELECT COALESCE(MAX(message_no),0) FROM {Msg}.chat_messages WHERE conversation_id=@p0 AND topic_id IS NULL"
            : $"SELECT COALESCE(MAX(message_no),0) FROM {Msg}.chat_messages WHERE conversation_id=@p0 AND topic_id=@p1";
        long max;
        await using (var command = topicId is null ? Command(maxSql, conversationId) : Command(maxSql, conversationId, topicId))
        {
            var value = await command.ExecuteScalarAsync(ct);
            max = value is long number ? number : value is int small ? small : 0;
        }
        await ExecuteAsync($"""
            INSERT INTO {Msg}.group_topic_reads(community_id,topic_id,user_id,last_read_no)
            VALUES(@p0,@p1,@p2,@p3)
            ON CONFLICT (community_id, topic_id, user_id) DO UPDATE
            SET last_read_no = GREATEST({Msg}.group_topic_reads.last_read_no, EXCLUDED.last_read_no)
            """, communityId, readKey, UserId, max);
    }
}
