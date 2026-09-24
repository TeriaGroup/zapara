using System.Text;
using System.Text.Json;
using Npgsql;
using Zapara.Server.Accounts;
using Zapara.Server.Social;

namespace Zapara.Server.Operator;

public sealed class SupportStore(AccountsDataSource data, IConfiguration configuration, IObjectStore objects, StudentUpload? uploads = null, QuotaLedger? ledger = null, IAccountUnitOfWork? accounts = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SupportTicket> OpenAsync(Guid userId, string subject, string body, CancellationToken ct, IReadOnlyList<SupportFile>? files = null, string? token = null)
    {
        var cleanSubject = subject.Trim();
        var cleanBody = body.Trim();
        if (cleanSubject.Length is < 3 or > 120 || cleanBody.Length is < 3 or > 4000)
            throw new AccountBodyException();
        var batch = files ?? [];
        SupportFiles.CheckCounts(batch);
        var at = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();
        var stored = await KeepFiles(batch, token, ct);
        var ticket = new SupportTicket(Guid.NewGuid(), userId.ToString("D"), cleanSubject, [new("user", cleanBody, at, Names(stored))]);
        try
        {
            await using var connection = await Open(ct);
            await Insert(connection, ticket, messageId, stored, ct);
        }
        catch
        {
            await Undo(stored, token, ct);
            throw;
        }
        Keep(messageId, cleanBody);
        return ticket;
    }

    public async Task<SupportTicket> ContinueAsync(Guid userId, Guid id, string body, CancellationToken ct, IReadOnlyList<SupportFile>? files = null, string? token = null)
    {
        var clean = body.Trim();
        if (clean.Length is < 1 or > 4000) throw new AccountBodyException();
        var batch = files ?? [];
        SupportFiles.CheckCounts(batch);
        var stored = await KeepFiles(batch, token, ct);
        await using var connection = await Open(ct);
        var schema = OperatorSettings.Schema(configuration);
        var messageId = Guid.NewGuid();
        try
        {
            await using var tx = await connection.BeginTransactionAsync(ct);
            await using var command = new NpgsqlCommand($"""
                INSERT INTO {schema}.support_messages(message_id, thread_id, author, body, created_at)
                SELECT @id, thread_id, 'user', @body, now() FROM {schema}.support_threads
                WHERE thread_id = @thread AND user_id = @user
                """, connection, tx);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("thread", id);
            command.Parameters.AddWithValue("user", userId);
            command.Parameters.AddWithValue("body", clean);
            if (await command.ExecuteNonQueryAsync(ct) != 1) throw new AccountServiceException(AccountFailure.InvalidRequest);
            await InsertFiles(connection, tx, messageId, stored, ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await Undo(stored, token, ct);
            throw;
        }
        Keep(messageId, clean);
        return (await ListAsync(userId, ct)).Single(ticket => ticket.Id == id);
    }

    public async Task<IReadOnlyList<SupportTicket>> ListAsync(Guid? userId, CancellationToken ct)
    {
        await using var connection = await Open(ct);
        var schema = OperatorSettings.Schema(configuration);
        await using var command = new NpgsqlCommand($"""
            SELECT t.thread_id, t.user_id, t.subject, m.author, m.body, m.created_at, m.message_id
            FROM {schema}.support_threads t
            JOIN {schema}.support_messages m ON m.thread_id = t.thread_id
            {(userId is null ? "" : "WHERE t.user_id = @user")}
            ORDER BY t.created_at, m.created_at, m.message_id
            """, connection);
        if (userId is not null) command.Parameters.AddWithValue("user", userId.Value);
        var rows = new List<(Guid Thread, string User, string Subject, Guid Message, string Author, string Body, DateTimeOffset At)>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                rows.Add((reader.GetGuid(0), reader.GetGuid(1).ToString("D"), reader.GetString(2), reader.GetGuid(6), reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5)));
        }
        var files = await Files(connection, rows.Select(row => row.Message).ToArray(), ct);
        var map = new Dictionary<Guid, SupportBag>();
        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.Thread, out var bag))
            {
                bag = new SupportBag(row.User, row.Subject);
                map[row.Thread] = bag;
            }
            var text = ReadBody(row.Message, row.Body);
            files.TryGetValue(row.Message, out var attached);
            bag.Lines.Add(new(row.Author, text, row.At, attached));
        }
        return map.Select(pair => new SupportTicket(pair.Key, pair.Value.User, pair.Value.Subject, pair.Value.Lines)).ToArray();
    }

    public async Task<(byte[] Bytes, string Type, string Name, string Kind)?> ReadFileAsync(Guid userId, Guid attachmentId, CancellationToken ct)
    {
        await using var connection = await Open(ct);
        var schema = OperatorSettings.Schema(configuration);
        await using var command = new NpgsqlCommand($"""
            SELECT a.content_type, a.file_name, a.kind, a.payload
            FROM {schema}.support_attachments a
            JOIN {schema}.support_messages m ON m.message_id = a.message_id
            JOIN {schema}.support_threads t ON t.thread_id = m.thread_id
            WHERE a.attachment_id = @id AND t.user_id = @user
            """, connection);
        command.Parameters.AddWithValue("id", attachmentId);
        command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var type = reader.GetString(0);
        var name = reader.GetString(1);
        var kind = reader.GetString(2);
        var payload = (byte[])reader.GetValue(3);
        var stored = objects.Get(ContentNames.SupportFile(attachmentId));
        return (stored ?? payload, type, name, kind);
    }

    public async Task<SupportTicket> ReplyAsync(Guid id, string body, CancellationToken ct)
    {
        var clean = body.Trim();
        if (clean.Length is < 1 or > 4000) throw new AccountBodyException();
        await using var connection = await Open(ct);
        var schema = OperatorSettings.Schema(configuration);
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {schema}.support_messages(message_id, thread_id, author, body, created_at)
            SELECT @id, thread_id, 'operator', @body, now() FROM {schema}.support_threads WHERE thread_id = @thread
            """, connection);
        var messageId = Guid.NewGuid();
        command.Parameters.AddWithValue("id", messageId);
        command.Parameters.AddWithValue("thread", id);
        command.Parameters.AddWithValue("body", clean);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new AccountServiceException(AccountFailure.InvalidRequest);
        Keep(messageId, clean);
        return (await ListAsync(null, ct)).Single(ticket => ticket.Id == id);
    }

    private async Task<NpgsqlConnection> Open(CancellationToken ct)
    {
        var connection = data.CreateConnection();
        await connection.OpenAsync(ct);
        var schema = OperatorSettings.Schema(configuration);
        foreach (var sql in new[]
        {
            $"CREATE SCHEMA IF NOT EXISTS {schema}",
            $"CREATE TABLE IF NOT EXISTS {schema}.support_threads (thread_id uuid PRIMARY KEY, user_id uuid NOT NULL, subject text NOT NULL, created_at timestamptz NOT NULL)",
            $"CREATE TABLE IF NOT EXISTS {schema}.support_messages (message_id uuid PRIMARY KEY, thread_id uuid NOT NULL, author text NOT NULL, body text NOT NULL, created_at timestamptz NOT NULL)",
            $"CREATE TABLE IF NOT EXISTS {schema}.support_attachments (attachment_id uuid PRIMARY KEY, message_id uuid NOT NULL, kind text NOT NULL, file_name text NOT NULL, content_type text NOT NULL, byte_count integer NOT NULL, payload bytea NOT NULL)",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(ct);
        }
        return connection;
    }

    private async Task<List<(Guid Id, string Key, byte[] Bytes, SupportFile File)>> KeepFiles(IReadOnlyList<SupportFile> files, string? token, CancellationToken ct)
    {
        var stored = new List<(Guid Id, string Key, byte[] Bytes, SupportFile File)>();
        if (files.Count == 0) return stored;
        if (uploads is null || ledger is null || accounts is null || string.IsNullOrEmpty(token))
            throw new SocialException(503, "storage_unavailable");
        try
        {
            foreach (var file in files)
            {
                var id = Guid.NewGuid();
                var key = ContentNames.SupportFile(id);
                byte[] kept;
                try { kept = await uploads.Accept(accounts, token, null, key, file.Bytes, ct); }
                catch (Exception ex) when (ex is not SocialException)
                {
                    throw new SocialException(503, "storage_unavailable");
                }
                stored.Add((id, key, kept, file));
            }
        }
        catch
        {
            await Undo(stored, token, ct);
            throw;
        }
        return stored;
    }

    private async Task Undo(IReadOnlyList<(Guid Id, string Key, byte[] Bytes, SupportFile File)> stored, string? token, CancellationToken ct)
    {
        foreach (var item in stored)
        {
            try { objects.Delete(item.Key); } catch (Exception) { /* The quota release still runs. */ }
            if (ledger is null || accounts is null || string.IsNullOrEmpty(token)) continue;
            try { await ledger.Release(accounts, token, item.Bytes.LongLength, null, ct); }
            catch (Exception) { /* A failed release leaves the counter high rather than dropping a later upload. */ }
        }
    }

    private static IReadOnlyList<SupportAttachment> Names(IReadOnlyList<(Guid Id, string Key, byte[] Bytes, SupportFile File)> stored)
        => stored.Select(item => new SupportAttachment(item.Id, item.File.Kind, item.File.Name)).ToArray();

    private void Keep(Guid messageId, string body)
    {
        try { objects.Put(ContentNames.Support(messageId), Encoding.UTF8.GetBytes(body)); }
        catch (Exception) { /* The thread row remains readable from the database. */ }
    }

    private string ReadBody(Guid messageId, string fallback)
    {
        try
        {
            var key = ContentNames.Support(messageId);
            var stored = objects.Get(key);
            if (stored is null)
            {
                stored = Encoding.UTF8.GetBytes(fallback);
                objects.Put(key, stored);
            }
            return Encoding.UTF8.GetString(stored);
        }
        catch (Exception) { return fallback; }
    }

    private async Task Insert(NpgsqlConnection connection, SupportTicket ticket, Guid messageId, IReadOnlyList<(Guid Id, string Key, byte[] Bytes, SupportFile File)> files, CancellationToken ct)
    {
        var schema = OperatorSettings.Schema(configuration);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var thread = new NpgsqlCommand($"INSERT INTO {schema}.support_threads(thread_id, user_id, subject, created_at) VALUES(@id, @user, @subject, @at)", connection, tx);
        thread.Parameters.AddWithValue("id", ticket.Id);
        thread.Parameters.AddWithValue("user", Guid.Parse(ticket.UserId));
        thread.Parameters.AddWithValue("subject", ticket.Subject);
        thread.Parameters.AddWithValue("at", ticket.Messages[0].At);
        await thread.ExecuteNonQueryAsync(ct);
        await using var message = new NpgsqlCommand($"INSERT INTO {schema}.support_messages(message_id, thread_id, author, body, created_at) VALUES(@id, @thread, 'user', @body, @at)", connection, tx);
        message.Parameters.AddWithValue("id", messageId);
        message.Parameters.AddWithValue("thread", ticket.Id);
        message.Parameters.AddWithValue("body", ticket.Messages[0].Body);
        message.Parameters.AddWithValue("at", ticket.Messages[0].At);
        await message.ExecuteNonQueryAsync(ct);
        await InsertFiles(connection, tx, messageId, files, ct);
        await tx.CommitAsync(ct);
    }

    private async Task InsertFiles(NpgsqlConnection connection, NpgsqlTransaction tx, Guid messageId, IReadOnlyList<(Guid Id, string Key, byte[] Bytes, SupportFile File)> files, CancellationToken ct)
    {
        var schema = OperatorSettings.Schema(configuration);
        foreach (var file in files)
        {
            await using var command = new NpgsqlCommand($"""
                INSERT INTO {schema}.support_attachments(attachment_id, message_id, kind, file_name, content_type, byte_count, payload)
                VALUES(@id, @message, @kind, @name, @type, @count, @payload)
                """, connection, tx);
            command.Parameters.AddWithValue("id", file.Id);
            command.Parameters.AddWithValue("message", messageId);
            command.Parameters.AddWithValue("kind", file.File.Kind);
            command.Parameters.AddWithValue("name", file.File.Name);
            command.Parameters.AddWithValue("type", file.File.ContentType);
            command.Parameters.AddWithValue("count", file.Bytes.Length);
            command.Parameters.AddWithValue("payload", file.Bytes);
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task<Dictionary<Guid, SupportAttachment[]>> Files(NpgsqlConnection connection, Guid[] messages, CancellationToken ct)
    {
        var map = new Dictionary<Guid, List<SupportAttachment>>();
        if (messages.Length == 0) return [];
        var schema = OperatorSettings.Schema(configuration);
        await using var command = new NpgsqlCommand($"""
            SELECT message_id, attachment_id, kind, file_name
            FROM {schema}.support_attachments
            WHERE message_id = ANY(@ids)
            ORDER BY attachment_id
            """, connection);
        command.Parameters.AddWithValue("ids", messages);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var message = reader.GetGuid(0);
            if (!map.TryGetValue(message, out var list))
            {
                list = [];
                map[message] = list;
            }
            list.Add(new SupportAttachment(reader.GetGuid(1), reader.GetString(2), reader.GetString(3)));
        }
        return map.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    public static string Write(SupportTicket ticket) => JsonSerializer.Serialize(new
    {
        id = ticket.Id,
        subject = ticket.Subject,
        messages = ticket.Messages.Select(line => new
        {
            author = line.Author,
            body = line.Body,
            at = line.At,
            attachments = (line.Attachments ?? []).Select(file => new { id = file.Id, kind = file.Kind, name = file.Name })
        })
    }, Json);
}

sealed class SupportBag(string user, string subject)
{
    public string User { get; } = user;
    public string Subject { get; } = subject;
    public List<SupportLine> Lines { get; } = [];
}
