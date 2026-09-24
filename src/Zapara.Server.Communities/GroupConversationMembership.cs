using Npgsql;

namespace Zapara.Server.Communities;

public static class GroupConversationMembership
{
    public static async Task EnsureAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CommunitiesConfiguration configuration, Guid communityId, DateTimeOffset now, CancellationToken ct)
    {
        var messages = configuration.QuotedMessages;
        var communities = configuration.QuotedSchema;
        var id = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand($"""
            INSERT INTO {messages}.conversations(conversation_id,kind,community_id,direct_key,created_at)
            VALUES(@id,'group',@community,NULL,@now) ON CONFLICT DO NOTHING
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("community", communityId);
            insert.Parameters.AddWithValue("now", now);
            await insert.ExecuteNonQueryAsync(ct);
        }
        await using (var lookup = new NpgsqlCommand($"SELECT conversation_id FROM {messages}.conversations WHERE kind='group' AND community_id=@community", connection, transaction))
        {
            lookup.Parameters.AddWithValue("community", communityId);
            id = (Guid)(await lookup.ExecuteScalarAsync(ct))!;
        }
        await using var members = new NpgsqlCommand($"""
            INSERT INTO {messages}.conversation_members(conversation_id,user_id,joined_at,last_read_no)
            SELECT @id, user_id, @now, COALESCE((SELECT MAX(message_no) FROM {messages}.chat_messages WHERE conversation_id=@id),0)
            FROM {communities}.memberships WHERE community_id=@community AND status='active'
            ON CONFLICT DO NOTHING
            """, connection, transaction);
        members.Parameters.AddWithValue("id", id);
        members.Parameters.AddWithValue("now", now);
        members.Parameters.AddWithValue("community", communityId);
        await members.ExecuteNonQueryAsync(ct);
    }
}
