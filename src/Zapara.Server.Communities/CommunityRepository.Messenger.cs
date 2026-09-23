using Npgsql;
using Zapara.Contracts.Communities;

namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository
{
    private string Msg => configuration.QuotedMessages;
    private const int PageSize = 50;

    internal async Task<GroupHomeResponse> GroupHomeAsync(Guid communityId)
    {
        await RequireMemberAsync(communityId);
        var conversationId = await EnsureGroupConversationAsync(communityId);
        string name;
        string? groupName;
        await using (var command = Command($"""
            SELECT c.name, m.group_name FROM {Schema}.communities c
            LEFT JOIN {Schema}.catalog_maps m ON m.community_id=c.community_id
            WHERE c.community_id=@p0
            """, communityId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw CommunityServiceException.NotFound();
            name = reader.GetString(0);
            groupName = reader.IsDBNull(1) ? null : reader.GetString(1);
        }
        var classmates = new List<ClassmateResponse>();
        await using (var command = Command($"""
            SELECT u.user_id, u.username, u.display_name, mb.role
            FROM {Schema}.memberships mb
            JOIN {configuration.Accounts.QuotedSchema}.users u ON u.user_id=mb.user_id
            WHERE mb.community_id=@p0 AND mb.status='active'
            ORDER BY CASE mb.role WHEN 'curator' THEN 0 WHEN 'headman' THEN 1 ELSE 2 END, coalesce(u.display_name, u.username), u.user_id
            """, communityId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                classmates.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.GetGuid(0) == UserId));
        var preview = await PreviewAsync(conversationId);
        var chat = new ConversationResponse(conversationId, "group", communityId, name, null, preview.Body, preview.At, preview.Unread);
        return new(communityId, name, groupName, chat, classmates, await DirectsAsync(communityId));
    }

    internal async Task<ConversationResponse> OpenDirectAsync(Guid communityId, Guid peerId)
    {
        if (peerId == UserId) throw CommunityServiceException.InvalidRequest();
        await RequireMemberAsync(communityId);
        if (!await ActiveMemberAsync(communityId, peerId)) throw CommunityServiceException.NotFound();
        var key = DirectKey(UserId, peerId);
        var id = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO {Msg}.conversations(conversation_id,kind,community_id,direct_key,created_at)
            VALUES(@p0,'direct',@p1,@p2,@p3) ON CONFLICT DO NOTHING
            """, id, communityId, key, Now);
        await using (var command = Command($"""
            SELECT conversation_id FROM {Msg}.conversations
            WHERE kind='direct' AND community_id=@p0 AND direct_key=@p1
            """, communityId, key))
            id = (Guid)(await command.ExecuteScalarAsync(ct))!;
        await ExecuteAsync($"""
            INSERT INTO {Msg}.conversation_members(conversation_id,user_id,joined_at,last_read_no)
            VALUES(@p0,@p1,@p2,COALESCE((SELECT MAX(message_no) FROM {Msg}.chat_messages WHERE conversation_id=@p0),0)),
                   (@p0,@p3,@p2,COALESCE((SELECT MAX(message_no) FROM {Msg}.chat_messages WHERE conversation_id=@p0),0))
            ON CONFLICT DO NOTHING
            """, id, UserId, Now, peerId);
        return await DescribeAsync(id, await DisplayNameAsync(peerId), peerId);
    }

    internal async Task<ChatPageResponse> ListMessagesAsync(Guid conversationId, Guid? before, Guid? after, string? topic = null)
    {
        if (before is not null && after is not null) throw CommunityServiceException.InvalidRequest();
        await RequireConversationAsync(conversationId);
        Guid? topicId = null;
        var scoped = false;
        if (!string.IsNullOrWhiteSpace(topic))
        {
            scoped = true;
            if (topic == "general") topicId = null;
            else if (Guid.TryParseExact(topic, "D", out var parsed) && parsed != Guid.Empty) topicId = parsed;
            else throw CommunityServiceException.InvalidRequest();
            var info = await ConversationInfoAsync(conversationId);
            if (info.Kind != "group") throw CommunityServiceException.InvalidRequest();
            if (topicId is not null && !await ExistsAsync($"SELECT topic_id FROM {Msg}.group_topics WHERE topic_id=@p0 AND community_id=@p1", topicId, info.CommunityId))
                throw CommunityServiceException.NotFound();
        }
        long? cursor = null;
        if (before is not null || after is not null)
        {
            cursor = await MessageNoAsync(conversationId, (before ?? after)!.Value);
            if (cursor is null) throw CommunityServiceException.InvalidRequest();
        }
        var older = before is not null;
        var order = cursor is null || older ? "DESC" : "ASC";
        var parameters = new List<object?> { conversationId };
        var topicClause = "";
        if (scoped && topicId is null) topicClause = " AND m.topic_id IS NULL";
        else if (scoped)
        {
            topicClause = " AND m.topic_id=@p" + parameters.Count;
            parameters.Add(topicId);
        }
        var comparison = "";
        if (cursor is not null)
        {
            comparison = older ? " AND m.message_no < @p" + parameters.Count : " AND m.message_no > @p" + parameters.Count;
            parameters.Add(cursor.Value);
        }
        var list = new List<ChatMessageResponse>();
        var sql = $"""
            SELECT m.message_id, m.conversation_id, m.sender_id, coalesce(u.display_name, u.username), m.body, m.created_at, m.kind, m.deleted, m.reply_to
            FROM {Msg}.chat_messages m
            JOIN {configuration.Accounts.QuotedSchema}.users u ON u.user_id=m.sender_id
            WHERE m.conversation_id=@p0{topicClause}{comparison}
            ORDER BY m.message_no {order}
            LIMIT {PageSize + 1}
            """;
        await using (var command = Command(sql, parameters.ToArray()))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                list.Add(ReadMessage(reader));
        var hasMore = list.Count > PageSize;
        if (hasMore) list.RemoveAt(list.Count - 1);
        if (order == "DESC") list.Reverse();
        if (scoped)
        {
            var info = await ConversationInfoAsync(conversationId);
            await MarkTopicReadAsync(info.CommunityId, conversationId, topicId);
        }
        return new(list, hasMore);
    }

    internal async Task<ChatMessageResponse> SendMessageAsync(Guid conversationId, string body, Guid? replyTo = null, string kind = "text")
    {
        body = Clean(body);
        if (kind is not ("text" or "image" or "video" or "file" or "voice" or "circle")) throw CommunityServiceException.InvalidRequest();
        await RequireConversationAsync(conversationId);
        if (replyTo is Guid parent && await MessageNoAsync(conversationId, parent) is null) throw CommunityServiceException.InvalidRequest();
        var id = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO {Msg}.chat_messages(message_id,conversation_id,sender_id,body,created_at,kind,reply_to)
            VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)
            """, id, conversationId, UserId, body, Now, kind, replyTo);
        return new(id, conversationId, UserId, await DisplayNameAsync(UserId), body, Now, kind, false, replyTo);
    }

    internal async Task<ChatMessageResponse> EditMessageAsync(Guid conversationId, Guid messageId, string body)
    {
        body = Clean(body);
        await RequireConversationAsync(conversationId);
        await using (var command = Command($"""
            UPDATE {Msg}.chat_messages SET body=@p0
            WHERE message_id=@p1 AND conversation_id=@p2 AND sender_id=@p3 AND deleted=false AND kind='text'
            """, body, messageId, conversationId, UserId))
            if (await command.ExecuteNonQueryAsync(ct) != 1) throw CommunityServiceException.InvalidRequest();
        return await ReadOneAsync(conversationId, messageId);
    }

    internal async Task<ChatMessageResponse> OpenMediaAsync(Guid conversationId, Guid messageId)
    {
        await RequireConversationAsync(conversationId);
        var message = await ReadOneAsync(conversationId, messageId);
        if (message.Deleted || message.Kind is not ("image" or "video" or "file")) throw CommunityServiceException.NotFound();
        return message;
    }

    internal async Task<ChatMessageResponse> DeleteMessageAsync(Guid conversationId, Guid messageId)
    {
        await RequireConversationAsync(conversationId);
        await using (var command = Command($"""
            UPDATE {Msg}.chat_messages SET deleted=true
            WHERE message_id=@p0 AND conversation_id=@p1 AND sender_id=@p2 AND deleted=false
            """, messageId, conversationId, UserId))
            if (await command.ExecuteNonQueryAsync(ct) != 1) throw CommunityServiceException.InvalidRequest();
        return await ReadOneAsync(conversationId, messageId);
    }

    internal async Task<ChatMessageResponse> ReactMessageAsync(Guid conversationId, Guid messageId, string emoji)
    {
        if (emoji is not ("like" or "heart" or "laugh" or "wow" or "sad")) throw CommunityServiceException.InvalidRequest();
        await RequireConversationAsync(conversationId);
        if (await MessageNoAsync(conversationId, messageId) is null) throw CommunityServiceException.InvalidRequest();
        await ExecuteAsync($"""
            INSERT INTO {Msg}.chat_reactions(message_id,user_id,emoji) VALUES(@p0,@p1,@p2)
            ON CONFLICT (message_id, user_id) DO UPDATE SET emoji=EXCLUDED.emoji
            """, messageId, UserId, emoji);
        return await ReadOneAsync(conversationId, messageId);
    }

    internal async Task<ConversationResponse> MarkReadAsync(Guid conversationId)
    {
        await RequireConversationAsync(conversationId);
        await ExecuteAsync($"""
            UPDATE {Msg}.conversation_members
            SET last_read_no=COALESCE((SELECT MAX(message_no) FROM {Msg}.chat_messages WHERE conversation_id=@p0),0)
            WHERE conversation_id=@p0 AND user_id=@p1
            """, conversationId, UserId);
        return await DescribeAsync(conversationId, await TitleAsync(conversationId), await PeerAsync(conversationId));
    }

    private async Task<Guid> EnsureGroupConversationAsync(Guid communityId)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO {Msg}.conversations(conversation_id,kind,community_id,direct_key,created_at)
            VALUES(@p0,'group',@p1,NULL,@p2) ON CONFLICT DO NOTHING
            """, id, communityId, Now);
        await using (var command = Command($"SELECT conversation_id FROM {Msg}.conversations WHERE kind='group' AND community_id=@p0", communityId))
            id = (Guid)(await command.ExecuteScalarAsync(ct))!;
        await SyncGroupMembersAsync(id, communityId);
        return id;
    }

    private async Task SyncGroupMembersAsync(Guid conversationId, Guid communityId)
    {
        await ExecuteAsync($"""
            DELETE FROM {Msg}.conversation_members
            WHERE conversation_id=@p0 AND user_id NOT IN (
                SELECT user_id FROM {Schema}.memberships WHERE community_id=@p1 AND status='active')
            """, conversationId, communityId);
        await ExecuteAsync($"""
            INSERT INTO {Msg}.conversation_members(conversation_id,user_id,joined_at,last_read_no)
            SELECT @p0, user_id, @p1, COALESCE((SELECT MAX(message_no) FROM {Msg}.chat_messages WHERE conversation_id=@p0),0)
            FROM {Schema}.memberships
            WHERE community_id=@p2 AND status='active'
            ON CONFLICT DO NOTHING
            """, conversationId, Now, communityId);
    }

    private async Task RequireConversationAsync(Guid conversationId)
    {
        string kind;
        Guid communityId;
        await using (var command = Command($"SELECT kind, community_id FROM {Msg}.conversations WHERE conversation_id=@p0", conversationId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw CommunityServiceException.NotFound();
            kind = reader.GetString(0);
            communityId = reader.GetGuid(1);
        }
        await RequireMemberAsync(communityId);
        if (kind == "group") await SyncGroupMembersAsync(conversationId, communityId);
        else if (!await ActiveMemberAsync(communityId, await PeerAsync(conversationId) ?? throw CommunityServiceException.NotFound()))
            throw CommunityServiceException.NotFound();
        if (!await ExistsAsync($"SELECT 1 FROM {Msg}.conversation_members WHERE conversation_id=@p0 AND user_id=@p1", conversationId, UserId))
            throw CommunityServiceException.NotFound();
    }

    private async Task<IReadOnlyList<ConversationResponse>> DirectsAsync(Guid communityId)
    {
        var rows = new List<(Guid Id, Guid Peer, string Title)>();
        await using (var command = Command($"""
            SELECT c.conversation_id, peer.user_id, coalesce(u.display_name, u.username)
            FROM {Msg}.conversations c
            JOIN {Msg}.conversation_members mine ON mine.conversation_id=c.conversation_id AND mine.user_id=@p1
            JOIN {Msg}.conversation_members peer ON peer.conversation_id=c.conversation_id AND peer.user_id<>@p1
            JOIN {configuration.Accounts.QuotedSchema}.users u ON u.user_id=peer.user_id
            JOIN {Schema}.memberships mb ON mb.community_id=c.community_id AND mb.user_id=peer.user_id AND mb.status='active'
            WHERE c.kind='direct' AND c.community_id=@p0
            ORDER BY coalesce(u.display_name, u.username), c.conversation_id
            """, communityId, UserId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct)) rows.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2)));
        }
        var list = new List<ConversationResponse>(rows.Count);
        foreach (var row in rows) list.Add(await DescribeAsync(row.Id, row.Title, row.Peer));
        return list;
    }

    private async Task<ConversationResponse> DescribeAsync(Guid conversationId, string title, Guid? peerId)
    {
        string kind;
        Guid communityId;
        await using (var command = Command($"SELECT kind, community_id FROM {Msg}.conversations WHERE conversation_id=@p0", conversationId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw CommunityServiceException.NotFound();
            kind = reader.GetString(0);
            communityId = reader.GetGuid(1);
        }
        var preview = await PreviewAsync(conversationId);
        return new(conversationId, kind, communityId, title, kind == "direct" ? peerId : null, preview.Body, preview.At, preview.Unread);
    }

    private async Task<(string? Body, DateTimeOffset? At, int Unread)> PreviewAsync(Guid conversationId)
    {
        string? body = null;
        DateTimeOffset? at = null;
        await using (var command = Command($"""
            SELECT body, created_at FROM {Msg}.chat_messages
            WHERE conversation_id=@p0 ORDER BY message_no DESC LIMIT 1
            """, conversationId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            if (await reader.ReadAsync(ct))
            {
                body = reader.GetString(0);
                at = reader.GetFieldValue<DateTimeOffset>(1).ToUniversalTime();
            }
        int unread;
        await using (var command = Command($"""
            SELECT count(*) FROM {Msg}.chat_messages m
            JOIN {Msg}.conversation_members mine ON mine.conversation_id=m.conversation_id AND mine.user_id=@p1
            WHERE m.conversation_id=@p0 AND m.sender_id<>@p1 AND m.message_no > mine.last_read_no
            """, conversationId, UserId))
            unread = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        return (body, at, unread);
    }

    private async Task<string> TitleAsync(Guid conversationId)
    {
        await using var command = Command($"""
            SELECT CASE WHEN c.kind='group' THEN cm.name ELSE coalesce(u.display_name, u.username) END
            FROM {Msg}.conversations c
            JOIN {Schema}.communities cm ON cm.community_id=c.community_id
            LEFT JOIN {Msg}.conversation_members peer ON peer.conversation_id=c.conversation_id AND peer.user_id<>@p1
            LEFT JOIN {configuration.Accounts.QuotedSchema}.users u ON u.user_id=peer.user_id
            WHERE c.conversation_id=@p0
            """, conversationId, UserId);
        return (string)(await command.ExecuteScalarAsync(ct))!;
    }

    private async Task<Guid?> PeerAsync(Guid conversationId)
    {
        await using var command = Command($"""
            SELECT user_id FROM {Msg}.conversation_members WHERE conversation_id=@p0 AND user_id<>@p1
            """, conversationId, UserId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is Guid peer ? peer : null;
    }

    private async Task<long?> MessageNoAsync(Guid conversationId, Guid messageId)
    {
        await using var command = Command($"""
            SELECT message_no FROM {Msg}.chat_messages WHERE conversation_id=@p0 AND message_id=@p1
            """, conversationId, messageId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is long number ? number : null;
    }

    private Task<bool> ActiveMemberAsync(Guid communityId, Guid userId)
        => ExistsAsync($"SELECT 1 FROM {Schema}.memberships WHERE community_id=@p0 AND user_id=@p1 AND status='active'", communityId, userId);

    private async Task<string> DisplayNameAsync(Guid userId)
    {
        await using var command = Command($"SELECT coalesce(display_name, username) FROM {configuration.Accounts.QuotedSchema}.users WHERE user_id=@p0", userId);
        return (string)(await command.ExecuteScalarAsync(ct))!;
    }

    private async Task<ChatMessageResponse> ReadOneAsync(Guid conversationId, Guid messageId)
    {
        await using var command = Command($"""
            SELECT m.message_id, m.conversation_id, m.sender_id, coalesce(u.display_name, u.username), m.body, m.created_at, m.kind, m.deleted, m.reply_to
            FROM {Msg}.chat_messages m
            JOIN {configuration.Accounts.QuotedSchema}.users u ON u.user_id=m.sender_id
            WHERE m.conversation_id=@p0 AND m.message_id=@p1
            """, conversationId, messageId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw CommunityServiceException.NotFound();
        return ReadMessage(reader);
    }

    private static string Clean(string body)
    {
        try { return CommunityValidation.Message(body); }
        catch (ArgumentException) { throw CommunityServiceException.InvalidRequest(); }
    }

    private static ChatMessageResponse ReadMessage(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
        reader.GetFieldValue<DateTimeOffset>(5).ToUniversalTime(), reader.GetString(6), reader.GetBoolean(7),
        reader.IsDBNull(8) ? null : reader.GetGuid(8));

    private static string DirectKey(Guid left, Guid right)
    {
        var a = left.ToString("D");
        var b = right.ToString("D");
        return string.CompareOrdinal(a, b) < 0 ? a + ":" + b : b + ":" + a;
    }
}
