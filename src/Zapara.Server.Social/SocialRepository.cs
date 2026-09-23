using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;
using Zapara.Contracts.Communities;
using Zapara.Contracts.Social;
using Zapara.Server.Accounts;

namespace Zapara.Server.Social;

internal sealed class SocialRepository(TrustedAccountContext context, string schema, string accounts, CancellationToken ct)
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private Guid Me => context.UserId;
    private DateTimeOffset Now => new(context.UtcNow.Ticks - context.UtcNow.Ticks % 10, TimeSpan.Zero);

    public async Task<SocialHomeResponse> HomeAsync()
    {
        var code = await EnsureCodeAsync();
        return new(code, await FriendsAsync(), await IncomingAsync(), await OutgoingAsync());
    }

    public async Task<SocialHomeResponse> InviteAsync(string code)
    {
        code = Normalize(code);
        var other = await FindByCodeAsync(code);
        if (other == Me) throw new SocialException(400, "invalid_request");
        await LockPairAsync(Me, other);
        var pair = Pair(Me, other);
        var existing = await OpenFriendshipAsync(pair);
        if (existing is { } row)
        {
            if (row.Status == "accepted") return await HomeAsync();
            if (row.Addressee == Me) await AcceptCoreAsync(row.Id);
            return await HomeAsync();
        }
        var id = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO {schema}.friendships(friendship_id,requester_id,addressee_id,status,pair_key,created_at)
            VALUES(@p0,@p1,@p2,'pending',@p3,@p4)
            """, id, Me, other, pair, Now);
        return await HomeAsync();
    }

    public async Task<SocialHomeResponse> AcceptAsync(Guid friendshipId)
    {
        await LockFriendshipAsync(friendshipId, addressee: true);
        await AcceptCoreAsync(friendshipId);
        return await HomeAsync();
    }

    public async Task<SocialHomeResponse> DeclineAsync(Guid friendshipId)
    {
        var updated = await ExecuteAsync($"""
            UPDATE {schema}.friendships SET status='declined'
            WHERE friendship_id=@p0 AND addressee_id=@p1 AND status='pending'
            """, friendshipId, Me);
        if (updated != 1) throw new SocialException(404, "not_found");
        return await HomeAsync();
    }

    public async Task<SocialPageResponse> MessagesAsync(Guid conversationId, Guid? before)
    {
        await RequireMemberAsync(conversationId);
        long? cursor = null;
        if (before is not null)
        {
            cursor = await MessageNoAsync(conversationId, before.Value);
            if (cursor is null) throw new SocialException(400, "invalid_request");
        }
        var sql = $"""
            {SelectMessages()}
            WHERE m.conversation_id=@p0 {(cursor is null ? "" : "AND m.message_no < @p2")}
            ORDER BY m.message_no DESC
            LIMIT 51
            """;
        var rows = await ReadRowsAsync(cursor is null ? Command(sql, conversationId, Me) : Command(sql, conversationId, Me, cursor.Value));
        var more = rows.Count > 50;
        if (more) rows.RemoveAt(rows.Count - 1);
        rows.Reverse();
        await ExecuteAsync($"""
            UPDATE {schema}.reads SET last_read_no=COALESCE((SELECT MAX(message_no) FROM {schema}.messages WHERE conversation_id=@p0),0)
            WHERE conversation_id=@p0 AND user_id=@p1
            """, conversationId, Me);
        return new(await MaterializeAsync(rows), more);
    }

    public async Task<SocialMessageResponse> SendTextAsync(Guid conversationId, string body, Guid? replyTo = null)
    {
        try { body = CommunityValidation.Message(body); }
        catch (ArgumentException) { throw new SocialException(400, "invalid_request"); }
        await RequireMemberAsync(conversationId);
        var reply = await ReplyTargetAsync(conversationId, replyTo);
        var id = Guid.NewGuid();
        await InsertMessageAsync(id, conversationId, "text", body, reply);
        return await ReadOneAsync(id);
    }

    public async Task<SocialMessageResponse> SendStickerAsync(Guid conversationId, string? sticker, Guid? replyTo = null)
    {
        if (!SocialStickers.Known(sticker)) throw new SocialException(400, "invalid_request");
        await RequireMemberAsync(conversationId);
        var reply = await ReplyTargetAsync(conversationId, replyTo);
        var id = Guid.NewGuid();
        await InsertMessageAsync(id, conversationId, "sticker", sticker, reply);
        return await ReadOneAsync(id);
    }

    public async Task<SocialMessageResponse> SendCardAsync(Guid conversationId, string? body, Guid? replyTo = null)
    {
        var card = ChatCards.Canonical(body);
        await RequireMemberAsync(conversationId);
        var reply = await ReplyTargetAsync(conversationId, replyTo);
        var id = Guid.NewGuid();
        await InsertMessageAsync(id, conversationId, "card", card, reply);
        return await ReadOneAsync(id);
    }

    public async Task<SocialMessageResponse> EditAsync(Guid conversationId, Guid messageId, string body)
    {
        try { body = CommunityValidation.Message(body); }
        catch (ArgumentException) { throw new SocialException(400, "invalid_request"); }
        var updated = await ExecuteAsync($"""
            UPDATE {schema}.messages SET body=@p0, edited_at=@p1
            WHERE message_id=@p2 AND conversation_id=@p3 AND sender_id=@p4 AND kind='text' AND deleted_at IS NULL
            """, body, Now, messageId, conversationId, Me);
        if (updated != 1) throw new SocialException(404, "not_found");
        return await ReadOneAsync(messageId);
    }

    public async Task<string[]> AttachmentNamesAsync(Guid conversationId, Guid messageId)
    {
        var names = new List<string>();
        await using var command = Command($"""
            SELECT a.stored_name FROM {schema}.attachments a
            JOIN {schema}.messages m ON m.message_id = a.message_id
            WHERE m.message_id=@p0 AND m.conversation_id=@p1 AND m.sender_id=@p2 AND m.deleted_at IS NULL
            """, messageId, conversationId, Me);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
        return names.ToArray();
    }

    public async Task<SocialMessageResponse> DeleteAsync(Guid conversationId, Guid messageId)
    {
        await ExecuteAsync($"""
            INSERT INTO {schema}.file_purge(stored_name, created_at)
            SELECT a.stored_name, @p2 FROM {schema}.attachments a
            JOIN {schema}.messages m ON m.message_id=a.message_id
            WHERE m.message_id=@p0 AND m.conversation_id=@p1 AND m.sender_id=@p3 AND m.deleted_at IS NULL
            ON CONFLICT (stored_name) DO NOTHING
            """, messageId, conversationId, Now, Me);
        await ExecuteAsync($"""
            DELETE FROM {schema}.attachments a USING {schema}.messages m
            WHERE a.message_id=m.message_id AND m.message_id=@p0 AND m.conversation_id=@p1 AND m.sender_id=@p2 AND m.deleted_at IS NULL
            """, messageId, conversationId, Me);
        await ExecuteAsync($"""
            DELETE FROM {schema}.reactions r USING {schema}.messages m
            WHERE r.message_id=m.message_id AND m.message_id=@p0 AND m.conversation_id=@p1 AND m.sender_id=@p2 AND m.deleted_at IS NULL
            """, messageId, conversationId, Me);
        var updated = await ExecuteAsync($"""
            UPDATE {schema}.messages SET body=NULL, deleted_at=@p0
            WHERE message_id=@p1 AND conversation_id=@p2 AND sender_id=@p3 AND deleted_at IS NULL
            """, Now, messageId, conversationId, Me);
        if (updated != 1) throw new SocialException(404, "not_found");
        return await ReadOneAsync(messageId);
    }

    public async Task<SocialMessageResponse> ReactAsync(Guid conversationId, Guid messageId, string? emoji)
    {
        emoji ??= "";
        if (emoji.Length > 0 && emoji is not ("like" or "heart" or "laugh" or "wow" or "sad"))
            throw new SocialException(400, "invalid_request");
        await using (var member = Command($"""
            SELECT 1 FROM {schema}.messages m
            JOIN {schema}.reads r ON r.conversation_id=m.conversation_id AND r.user_id=@p2
            JOIN {schema}.friendships f ON f.friendship_id = (
                SELECT friendship_id FROM {schema}.conversations WHERE conversation_id=m.conversation_id
            ) AND f.status='accepted'
            WHERE m.message_id=@p0 AND m.conversation_id=@p1 AND m.deleted_at IS NULL
            """, messageId, conversationId, Me))
        {
            if (await member.ExecuteScalarAsync(ct) is null or DBNull) throw new SocialException(404, "not_found");
        }
        if (emoji.Length == 0)
            await ExecuteAsync($"DELETE FROM {schema}.reactions WHERE message_id=@p0 AND user_id=@p1", messageId, Me);
        else
        {
            string? existing;
            await using (var current = Command($"SELECT emoji FROM {schema}.reactions WHERE message_id=@p0 AND user_id=@p1", messageId, Me))
                existing = await current.ExecuteScalarAsync(ct) as string;
            if (existing == emoji)
                await ExecuteAsync($"DELETE FROM {schema}.reactions WHERE message_id=@p0 AND user_id=@p1", messageId, Me);
            else
                await ExecuteAsync($"""
                    INSERT INTO {schema}.reactions(message_id,user_id,emoji,created_at) VALUES(@p0,@p1,@p2,@p3)
                    ON CONFLICT (message_id, user_id) DO UPDATE SET emoji=EXCLUDED.emoji, created_at=EXCLUDED.created_at
                    """, messageId, Me, emoji, Now);
        }
        return await ReadOneAsync(messageId);
    }

    public async Task<SocialMessageResponse> SendFileAsync(Guid conversationId, string kind, string stored, string original,
        string contentType, long bytes, int? width, int? height, Guid? replyTo = null, int? durationMs = null)
    {
        if (kind is not ("image" or "file" or "voice" or "circle") || bytes < 1) throw new SocialException(400, "invalid_request");
        durationMs = VoicePolicy.Duration(durationMs);
        await RequireMemberAsync(conversationId);
        var reply = await ReplyTargetAsync(conversationId, replyTo);
        var messageId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        await InsertMessageAsync(messageId, conversationId, kind, null, reply);
        await using (var command = new NpgsqlCommand($"""
            INSERT INTO {schema}.attachments(attachment_id,message_id,stored_name,original_name,content_type,byte_count,width,height,duration_ms)
            VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)
            """, context.Connection, context.Transaction))
        {
            command.Parameters.AddWithValue("p0", attachmentId);
            command.Parameters.AddWithValue("p1", messageId);
            command.Parameters.AddWithValue("p2", stored);
            command.Parameters.AddWithValue("p3", original);
            command.Parameters.AddWithValue("p4", contentType);
            command.Parameters.AddWithValue("p5", bytes);
            command.Parameters.Add(new NpgsqlParameter("p6", NpgsqlDbType.Integer) { Value = (object?)width ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("p7", NpgsqlDbType.Integer) { Value = (object?)height ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("p8", NpgsqlDbType.Integer) { Value = (object?)durationMs ?? DBNull.Value });
            await command.ExecuteNonQueryAsync(ct);
        }
        return await ReadOneAsync(messageId);
    }

    public async Task<(string Stored, string Name, string Type)> OpenAsync(Guid attachmentId)
    {
        await using var command = Command($"""
            SELECT a.stored_name, a.original_name, a.content_type
            FROM {schema}.attachments a
            JOIN {schema}.messages m ON m.message_id=a.message_id
            JOIN {schema}.reads r ON r.conversation_id=m.conversation_id AND r.user_id=@p1
            WHERE a.attachment_id=@p0 AND m.deleted_at IS NULL
            """, attachmentId, Me);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new SocialException(404, "not_found");
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    private async Task AcceptCoreAsync(Guid friendshipId)
    {
        var updated = await ExecuteAsync($"""
            UPDATE {schema}.friendships SET status='accepted'
            WHERE friendship_id=@p0 AND addressee_id=@p1 AND status='pending'
            """, friendshipId, Me);
        if (updated != 1)
        {
            await using var check = Command($"SELECT status FROM {schema}.friendships WHERE friendship_id=@p0", friendshipId);
            if (await check.ExecuteScalarAsync(ct) is not "accepted") throw new SocialException(404, "not_found");
        }
        var conversation = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO {schema}.conversations(conversation_id,friendship_id,created_at)
            VALUES(@p0,@p1,@p2) ON CONFLICT (friendship_id) DO NOTHING
            """, conversation, friendshipId, Now);
        await using var command = Command($"SELECT conversation_id FROM {schema}.conversations WHERE friendship_id=@p0", friendshipId);
        conversation = (Guid)(await command.ExecuteScalarAsync(ct))!;
        await ExecuteAsync($"""
            INSERT INTO {schema}.reads(conversation_id,user_id,last_read_no)
            SELECT @p0, requester_id, 0 FROM {schema}.friendships WHERE friendship_id=@p1
            ON CONFLICT DO NOTHING
            """, conversation, friendshipId);
        await ExecuteAsync($"""
            INSERT INTO {schema}.reads(conversation_id,user_id,last_read_no)
            SELECT @p0, addressee_id, 0 FROM {schema}.friendships WHERE friendship_id=@p1
            ON CONFLICT DO NOTHING
            """, conversation, friendshipId);
    }

    private async Task<string> EnsureCodeAsync()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await ExecuteAsync("SAVEPOINT social_code");
            try
            {
                await ExecuteAsync($"""
                    INSERT INTO {schema}.codes(user_id,code) VALUES(@p0,@p1) ON CONFLICT (user_id) DO NOTHING
                    """, Me, NewCode());
                await ExecuteAsync("RELEASE SAVEPOINT social_code");
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                await ExecuteAsync("ROLLBACK TO SAVEPOINT social_code");
                continue;
            }
            await using var command = Command($"SELECT code FROM {schema}.codes WHERE user_id=@p0", Me);
            if (await command.ExecuteScalarAsync(ct) is string code) return code;
        }
        throw new SocialException(503, "db_unavailable");
    }

    private async Task<Guid> FindByCodeAsync(string code)
    {
        await using var command = Command($"""
            SELECT c.user_id FROM {schema}.codes c
            JOIN {accounts}.users u ON u.user_id=c.user_id AND u.status='active'
            WHERE c.code=@p0
            """, code);
        if (await command.ExecuteScalarAsync(ct) is not Guid id) throw new SocialException(404, "not_found");
        return id;
    }

    private async Task LockPairAsync(Guid left, Guid right)
    {
        var ids = left.CompareTo(right) < 0 ? new[] { left, right } : new[] { right, left };
        await using var command = Command($"""
            SELECT user_id FROM {accounts}.users WHERE user_id = ANY(@p0) AND status='active' ORDER BY user_id FOR UPDATE
            """, ids);
        var found = 0;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) found++;
        if (found != 2) throw new SocialException(404, "not_found");
    }

    private async Task<(Guid Id, string Status, Guid Addressee)?> OpenFriendshipAsync(string pair)
    {
        await using var command = Command($"""
            SELECT friendship_id, status, addressee_id FROM {schema}.friendships
            WHERE pair_key=@p0 AND status IN ('pending','accepted')
            """, pair);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return (reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2));
    }

    private async Task LockFriendshipAsync(Guid id, bool addressee)
    {
        await using var command = Command($"""
            SELECT friendship_id FROM {schema}.friendships
            WHERE friendship_id=@p0 AND {(addressee ? "addressee_id" : "requester_id")}=@p1 AND status='pending'
            FOR UPDATE
            """, id, Me);
        if (await command.ExecuteScalarAsync(ct) is null or DBNull) throw new SocialException(404, "not_found");
    }

    private async Task RequireMemberAsync(Guid conversationId)
    {
        await using var command = Command($"""
            SELECT 1 FROM {schema}.reads r
            JOIN {schema}.conversations c ON c.conversation_id=r.conversation_id
            JOIN {schema}.friendships f ON f.friendship_id=c.friendship_id AND f.status='accepted'
            WHERE r.conversation_id=@p0 AND r.user_id=@p1
            """, conversationId, Me);
        if (await command.ExecuteScalarAsync(ct) is null or DBNull) throw new SocialException(404, "not_found");
    }

    private async Task<List<SocialFriendResponse>> FriendsAsync()
    {
        var list = new List<SocialFriendResponse>();
        await using var command = Command($"""
            SELECT u.user_id, u.username, u.display_name, c.conversation_id, last.body, last.kind, last.original_name, last.created_at, last.deleted_at,
                   (SELECT count(*) FROM {schema}.messages m
                    JOIN {schema}.reads r ON r.conversation_id=c.conversation_id AND r.user_id=@p0
                    WHERE m.conversation_id=c.conversation_id AND m.sender_id<>@p0 AND m.message_no > r.last_read_no)
            FROM {schema}.friendships f
            JOIN {schema}.conversations c ON c.friendship_id=f.friendship_id
            JOIN {accounts}.users u ON u.user_id = CASE WHEN f.requester_id=@p0 THEN f.addressee_id ELSE f.requester_id END
            LEFT JOIN LATERAL (
                SELECT m.body, m.kind, a.original_name, m.created_at, m.deleted_at
                FROM {schema}.messages m
                LEFT JOIN {schema}.attachments a ON a.message_id=m.message_id
                WHERE m.conversation_id=c.conversation_id
                ORDER BY m.message_no DESC LIMIT 1
            ) last ON true
            WHERE f.status='accepted' AND (f.requester_id=@p0 OR f.addressee_id=@p0)
            ORDER BY coalesce(last.created_at, c.created_at) DESC, u.username
            """, Me);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var kind = reader.IsDBNull(5) ? null : reader.GetString(5);
            var body = reader.IsDBNull(4) ? null : reader.GetString(4);
            var file = reader.IsDBNull(6) ? null : reader.GetString(6);
            var unread = reader.GetInt64(9);
            list.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetGuid(3), Preview(kind, body, file, !reader.IsDBNull(8)), reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                unread > int.MaxValue ? int.MaxValue : (int)unread));
        }
        return list;
    }

    private Task<List<SocialInviteResponse>> IncomingAsync() => InvitesAsync(incoming: true);

    private Task<List<SocialInviteResponse>> OutgoingAsync() => InvitesAsync(incoming: false);

    private async Task<List<SocialInviteResponse>> InvitesAsync(bool incoming)
    {
        var list = new List<SocialInviteResponse>();
        var who = incoming ? "f.requester_id" : "f.addressee_id";
        var mine = incoming ? "f.addressee_id" : "f.requester_id";
        await using var command = Command($"""
            SELECT f.friendship_id, u.username, u.display_name, f.created_at
            FROM {schema}.friendships f
            JOIN {accounts}.users u ON u.user_id={who}
            WHERE {mine}=@p0 AND f.status='pending'
            ORDER BY f.created_at
            """, Me);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));
        return list;
    }

    private async Task<long?> MessageNoAsync(Guid conversationId, Guid messageId)
    {
        await using var command = Command($"SELECT message_no FROM {schema}.messages WHERE conversation_id=@p0 AND message_id=@p1", conversationId, messageId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is long number ? number : null;
    }

    private async Task<SocialMessageResponse> ReadOneAsync(Guid messageId)
    {
        var rows = await ReadRowsAsync(Command($"""
            {SelectMessages()}
            WHERE m.message_id=@p0
            """, messageId, Me));
        if (rows.Count != 1) throw new SocialException(404, "not_found");
        return (await MaterializeAsync(rows))[0];
    }

    private string SelectMessages() => $"""
        SELECT m.message_id, m.sender_id, coalesce(u.display_name, u.username), m.kind,
               CASE WHEN m.deleted_at IS NULL THEN m.body ELSE NULL END,
               CASE WHEN m.deleted_at IS NULL THEN a.attachment_id ELSE NULL END,
               CASE WHEN m.deleted_at IS NULL THEN a.original_name ELSE NULL END,
               CASE WHEN m.deleted_at IS NULL THEN a.content_type ELSE NULL END,
               CASE WHEN m.deleted_at IS NULL THEN a.byte_count ELSE NULL END,
               m.created_at, m.reply_to, r.kind,
               CASE WHEN r.deleted_at IS NULL THEN r.body ELSE NULL END,
               CASE WHEN r.deleted_at IS NULL THEN ra.original_name ELSE NULL END,
               (r.message_id IS NOT NULL AND r.deleted_at IS NOT NULL),
               m.edited_at, (m.deleted_at IS NOT NULL),
               (m.sender_id=@p1 AND m.message_no <= COALESCE(peer.last_read_no, 0)),
               CASE WHEN m.deleted_at IS NULL THEN a.duration_ms ELSE NULL END
        FROM {schema}.messages m
        JOIN {accounts}.users u ON u.user_id=m.sender_id
        LEFT JOIN {schema}.attachments a ON a.message_id=m.message_id
        LEFT JOIN {schema}.messages r ON r.message_id=m.reply_to
        LEFT JOIN {schema}.attachments ra ON ra.message_id=r.message_id
        LEFT JOIN {schema}.reads peer ON peer.conversation_id=m.conversation_id AND peer.user_id<>@p1
        """;

    private async Task InsertMessageAsync(Guid id, Guid conversationId, string kind, string? body, Guid? reply)
    {
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {schema}.messages(message_id,conversation_id,sender_id,kind,body,created_at,reply_to)
            VALUES(@id,@conversation,@sender,@kind,@body,@at,@reply)
            """, context.Connection, context.Transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("conversation", conversationId);
        command.Parameters.AddWithValue("sender", Me);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.Add(new NpgsqlParameter("body", NpgsqlDbType.Text) { Value = (object?)body ?? DBNull.Value });
        command.Parameters.AddWithValue("at", Now);
        command.Parameters.Add(new NpgsqlParameter("reply", NpgsqlDbType.Uuid) { Value = (object?)reply ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<Guid?> ReplyTargetAsync(Guid conversationId, Guid? replyTo)
    {
        if (replyTo is null || replyTo == Guid.Empty) return null;
        await using var command = Command($"SELECT 1 FROM {schema}.messages WHERE message_id=@p0 AND conversation_id=@p1", replyTo, conversationId);
        if (await command.ExecuteScalarAsync(ct) is null or DBNull) throw new SocialException(400, "invalid_request");
        return replyTo;
    }

    private async Task<List<RawMessage>> ReadRowsAsync(NpgsqlCommand command)
    {
        var rows = new List<RawMessage>();
        await using (command)
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) rows.Add(RawMessage.Read(reader));
        return rows;
    }

    private async Task<List<SocialMessageResponse>> MaterializeAsync(List<RawMessage> rows)
    {
        var reactions = await ReactionsAsync(rows.Select(row => row.Id).ToArray());
        return rows.ConvertAll(row => row.ToMessage(reactions.TryGetValue(row.Id, out var list) ? list : []));
    }

    private async Task<Dictionary<Guid, List<SocialReactionResponse>>> ReactionsAsync(Guid[] ids)
    {
        var map = new Dictionary<Guid, List<SocialReactionResponse>>();
        if (ids.Length == 0) return map;
        await using var command = Command($"""
            SELECT message_id, emoji, count(*)::int, bool_or(user_id=@p1)
            FROM {schema}.reactions WHERE message_id = ANY(@p0)
            GROUP BY message_id, emoji
            ORDER BY message_id, emoji
            """, ids, Me);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            if (!map.TryGetValue(id, out var list)) map[id] = list = [];
            list.Add(new(reader.GetString(1), reader.GetInt32(2), reader.GetBoolean(3)));
        }
        return map;
    }

    private static string? Preview(string? kind, string? body, string? file, bool deleted = false)
    {
        if (deleted) return "Сообщение удалено";
        return kind switch
        {
            "image" => "Фото",
            "file" => file ?? "Документ",
            "voice" => "Голосовое",
            "circle" => "Кружок",
            "card" => ChatCards.Preview(body),
            "sticker" => SocialStickers.Title(body),
            _ => body
        };
    }

    private sealed class RawMessage
    {
        private RawMessage(Guid id, Guid senderId, string senderName, string kind, string? body, Guid? attachmentId,
            string? fileName, string? contentType, long? bytes, DateTimeOffset createdAt, Guid? replyTo, string? replyBody,
            DateTimeOffset? editedAt, bool deleted, bool read, int? durationMs)
        {
            Id = id;
            this.senderId = senderId;
            this.senderName = senderName;
            this.kind = kind;
            this.body = body;
            this.attachmentId = attachmentId;
            this.fileName = fileName;
            this.contentType = contentType;
            this.bytes = bytes;
            this.createdAt = createdAt;
            this.replyTo = replyTo;
            this.replyBody = replyBody;
            this.editedAt = editedAt;
            this.deleted = deleted;
            this.read = read;
            this.durationMs = durationMs;
        }

        public Guid Id { get; }
        private readonly Guid senderId;
        private readonly string senderName;
        private readonly string kind;
        private readonly string? body;
        private readonly Guid? attachmentId;
        private readonly string? fileName;
        private readonly string? contentType;
        private readonly long? bytes;
        private readonly DateTimeOffset createdAt;
        private readonly Guid? replyTo;
        private readonly string? replyBody;
        private readonly DateTimeOffset? editedAt;
        private readonly bool deleted;
        private readonly bool read;
        private readonly int? durationMs;

        public SocialMessageResponse ToMessage(IReadOnlyList<SocialReactionResponse> reactions) => new(
            Id, senderId, senderName, kind, body, attachmentId, fileName, contentType, bytes, createdAt,
            replyTo, replyBody, editedAt, deleted, read, durationMs, reactions);

        public static RawMessage Read(NpgsqlDataReader reader)
        {
            var replyDeleted = reader.GetBoolean(14);
            var replyKind = reader.IsDBNull(11) ? null : reader.GetString(11);
            var replyText = reader.IsDBNull(12) ? null : reader.GetString(12);
            var replyFile = reader.IsDBNull(13) ? null : reader.GetString(13);
            return new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8),
                reader.GetFieldValue<DateTimeOffset>(9),
                reader.IsDBNull(10) ? null : reader.GetGuid(10),
                reader.IsDBNull(10) ? null : Preview(replyKind, replyText, replyFile, replyDeleted),
                reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
                reader.GetBoolean(16), reader.GetBoolean(17),
                reader.IsDBNull(18) ? null : reader.GetInt32(18));
        }
    }

    private static string Pair(Guid left, Guid right)
    {
        var a = left.ToString("D");
        var b = right.ToString("D");
        return string.CompareOrdinal(a, b) < 0 ? a + ":" + b : b + ":" + a;
    }

    private static string Normalize(string? code)
    {
        var raw = (code ?? "").Trim().ToUpperInvariant().Replace('А', 'A').Replace('О', 'O');
        var text = string.Concat(raw.Where(ch => ch is not ' ' and not '-' and not '\u2013' and not '\u2014'));
        if (text.Length != 8 || text.Any(ch => !Alphabet.Contains(ch))) throw new SocialException(400, "invalid_request");
        return text;
    }

    private static string NewCode()
    {
        Span<char> chars = stackalloc char[8];
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }

    private NpgsqlCommand Command(string sql, params object?[] parameters)
    {
        var command = new NpgsqlCommand(sql, context.Connection, context.Transaction);
        for (var i = 0; i < parameters.Length; i++) command.Parameters.AddWithValue("p" + i, parameters[i] ?? DBNull.Value);
        return command;
    }

    private async Task<int> ExecuteAsync(string sql, params object?[] parameters)
    {
        await using var command = Command(sql, parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }
}
