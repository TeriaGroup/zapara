using Npgsql;
using Zapara.Contracts.Communities;

namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository
{
    // Reads for the built-in general thread. This id is not a row in group_topics.
    private static readonly Guid GeneralRead = new("00000000-0000-0000-0000-000000000001");

    internal async Task<GroupTopicListResponse> TopicsAsync(Guid communityId)
    {
        var role = await RequireMemberAsync(communityId);
        var conversationId = await EnsureGroupConversationAsync(communityId);
        var headman = role == "headman";
        var topics = new List<GroupTopicResponse>
        {
            await DescribeTopicAsync(communityId, conversationId, null, GroupTopicNames.GeneralTitle, GroupTopicNames.GeneralIcon, null, false)
        };
        var rows = new List<(Guid Id, string Title, string Icon, Guid? Author)>();
        await using (var command = Command($"SELECT topic_id, title, icon, created_by FROM {Msg}.group_topics WHERE community_id=@p0 ORDER BY title, topic_id", communityId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetGuid(3)));
        foreach (var row in rows)
            topics.Add(await DescribeTopicAsync(communityId, conversationId, row.Id, row.Title, row.Icon, row.Author, headman || row.Author == UserId));
        topics.Sort((left, right) =>
        {
            if (left.TopicId is null && right.TopicId is null) return 0;
            if (left.TopicId is null) return -1;
            if (right.TopicId is null) return 1;
            return Nullable.Compare(right.LastAt, left.LastAt);
        });
        return new(topics);
    }

    internal async Task<GroupTopicListResponse> CreateTopicAsync(Guid communityId, GroupTopicRequest request)
    {
        await RequireMemberAsync(communityId);
        var title = GroupTopicNames.Title(request?.Title) ?? throw CommunityServiceException.InvalidRequest();
        var icon = GroupTopicNames.Icon(request?.Icon) ?? throw CommunityServiceException.InvalidRequest();
        if (await ScalarAsync($"SELECT count(*)::int FROM {Msg}.group_topics WHERE community_id=@p0", communityId) >= 24)
            throw CommunityServiceException.InvalidRequest();
        try
        {
            await ExecuteAsync($"""
                INSERT INTO {Msg}.group_topics(topic_id,community_id,title,icon,created_by,created_at)
                VALUES(@p0,@p1,@p2,@p3,@p4,@p5)
                """, Guid.NewGuid(), communityId, title, icon, UserId, Now);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw CommunityServiceException.Conflict("revision_conflict"); }
        return await TopicsAsync(communityId);
    }

    internal async Task<GroupTopicListResponse> RenameTopicAsync(Guid communityId, Guid topicId, GroupTopicRequest request)
    {
        var role = await RequireMemberAsync(communityId);
        var title = GroupTopicNames.Title(request?.Title) ?? throw CommunityServiceException.InvalidRequest();
        var icon = GroupTopicNames.Icon(request?.Icon) ?? throw CommunityServiceException.InvalidRequest();
        var author = await TopicAuthorAsync(communityId, topicId) ?? throw CommunityServiceException.NotFound();
        if (role != "headman" && author != UserId) throw CommunityServiceException.Forbidden();
        try
        {
            var updated = await ExecuteCountAsync($"UPDATE {Msg}.group_topics SET title=@p0, icon=@p1 WHERE topic_id=@p2 AND community_id=@p3", title, icon, topicId, communityId);
            if (updated != 1) throw CommunityServiceException.NotFound();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw CommunityServiceException.Conflict("revision_conflict"); }
        return await TopicsAsync(communityId);
    }

    internal async Task<GroupTopicListResponse> DeleteTopicAsync(Guid communityId, Guid topicId)
    {
        var role = await RequireMemberAsync(communityId);
        var author = await TopicAuthorAsync(communityId, topicId) ?? throw CommunityServiceException.NotFound();
        if (role != "headman" && author != UserId) throw CommunityServiceException.Forbidden();
        var conversationId = await EnsureGroupConversationAsync(communityId);
        await ExecuteAsync($"DELETE FROM {Msg}.chat_messages WHERE conversation_id=@p0 AND topic_id=@p1", conversationId, topicId);
        await ExecuteAsync($"DELETE FROM {Msg}.group_topic_reads WHERE community_id=@p0 AND topic_id=@p1", communityId, topicId);
        await ExecuteAsync($"DELETE FROM {Msg}.group_topics WHERE topic_id=@p0 AND community_id=@p1", topicId, communityId);
        return await TopicsAsync(communityId);
    }

    internal async Task<ChatMessageResponse> SendTopicMessageAsync(Guid conversationId, string body, Guid? topicId)
    {
        body = CommunityValidation.Message(body);
        await RequireConversationAsync(conversationId);
        var info = await ConversationInfoAsync(conversationId);
        if (topicId is not null)
        {
            if (info.Kind != "group") throw CommunityServiceException.InvalidRequest();
            if (!await ExistsAsync($"SELECT topic_id FROM {Msg}.group_topics WHERE topic_id=@p0 AND community_id=@p1", topicId, info.CommunityId))
                throw CommunityServiceException.NotFound();
        }
        var id = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO {Msg}.chat_messages(message_id,conversation_id,sender_id,body,created_at,topic_id)
            VALUES(@p0,@p1,@p2,@p3,@p4,@p5)
            """, id, conversationId, UserId, body, Now, topicId);
        return new(id, conversationId, UserId, await DisplayNameAsync(UserId), body, Now);
    }

    private async Task<GroupTopicResponse> DescribeTopicAsync(Guid communityId, Guid conversationId, Guid? topicId, string title, string icon, Guid? author, bool canDelete)
    {
        var readKey = topicId ?? GeneralRead;
        string? lastBody = null;
        string? lastAuthor = null;
        DateTimeOffset? lastAt = null;
        var previewSql = topicId is null
            ? $"""
            SELECT m.body, coalesce(u.display_name, u.username), m.created_at
            FROM {Msg}.chat_messages m
            JOIN {configuration.Accounts.QuotedSchema}.users u ON u.user_id=m.sender_id
            WHERE m.conversation_id=@p0 AND m.topic_id IS NULL
            ORDER BY m.message_no DESC LIMIT 1
            """
            : $"""
            SELECT m.body, coalesce(u.display_name, u.username), m.created_at
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
        return new(topicId, title, icon, lastBody, lastAuthor, lastAt, unread, canDelete && topicId is not null);
    }

    private async Task<Guid?> TopicAuthorAsync(Guid communityId, Guid topicId)
    {
        await using var command = Command($"SELECT created_by FROM {Msg}.group_topics WHERE topic_id=@p0 AND community_id=@p1", topicId, communityId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return reader.IsDBNull(0) ? Guid.Empty : reader.GetGuid(0);
    }

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
