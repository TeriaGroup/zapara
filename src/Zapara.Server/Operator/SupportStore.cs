using System.Text;
using System.Text.Json;
using Npgsql;
using Zapara.Server.Accounts;
using Zapara.Server.Social;

namespace Zapara.Server.Operator;

public sealed class SupportStore(AccountsDataSource data, IConfiguration configuration, IObjectStore objects)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SupportTicket> OpenAsync(Guid userId, string subject, string body, CancellationToken ct)
    {
        var cleanSubject = subject.Trim();
        var cleanBody = body.Trim();
        if (cleanSubject.Length is < 3 or > 120 || cleanBody.Length is < 3 or > 4000)
            throw new AccountBodyException();
        var at = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();
        var ticket = new SupportTicket(Guid.NewGuid(), userId.ToString("D"), cleanSubject, [new("user", cleanBody, at)]);
        await using var connection = await Open(ct);
        await Insert(connection, ticket, messageId, ct);
        Keep(messageId, cleanBody);
        return Load(ticket);
    }

    public async Task<SupportTicket> ContinueAsync(Guid userId, Guid id, string body, CancellationToken ct)
    {
        var clean = body.Trim();
        if (clean.Length is < 1 or > 4000) throw new AccountBodyException();
        await using var connection = await Open(ct);
        var schema = OperatorSettings.Schema(configuration);
        var messageId = Guid.NewGuid();
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {schema}.support_messages(message_id, thread_id, author, body, created_at)
            SELECT @id, thread_id, 'user', @body, now() FROM {schema}.support_threads
            WHERE thread_id = @thread AND user_id = @user
            """, connection);
        command.Parameters.AddWithValue("id", messageId);
        command.Parameters.AddWithValue("thread", id);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("body", clean);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new AccountServiceException(AccountFailure.InvalidRequest);
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
        var map = new Dictionary<Guid, SupportBag>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            if (!map.TryGetValue(id, out var row))
            {
                row = new SupportBag(reader.GetGuid(1).ToString("D"), reader.GetString(2));
                map[id] = row;
            }
            var messageId = reader.GetGuid(6);
            var text = ReadBody(messageId, reader.GetString(4));
            row.Lines.Add(new(reader.GetString(3), text, reader.GetFieldValue<DateTimeOffset>(5)));
        }
        return map.Select(pair => new SupportTicket(pair.Key, pair.Value.User, pair.Value.Subject, pair.Value.Lines)).ToArray();
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
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(ct);
        }
        return connection;
    }

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

    private static SupportTicket Load(SupportTicket ticket) => ticket;

    private async Task Insert(NpgsqlConnection connection, SupportTicket ticket, Guid messageId, CancellationToken ct)
    {
        var schema = OperatorSettings.Schema(configuration);
        await using var thread = new NpgsqlCommand($"INSERT INTO {schema}.support_threads(thread_id, user_id, subject, created_at) VALUES(@id, @user, @subject, @at)", connection);
        thread.Parameters.AddWithValue("id", ticket.Id);
        thread.Parameters.AddWithValue("user", Guid.Parse(ticket.UserId));
        thread.Parameters.AddWithValue("subject", ticket.Subject);
        thread.Parameters.AddWithValue("at", ticket.Messages[0].At);
        await thread.ExecuteNonQueryAsync(ct);
        await using var message = new NpgsqlCommand($"INSERT INTO {schema}.support_messages(message_id, thread_id, author, body, created_at) VALUES(@id, @thread, 'user', @body, @at)", connection);
        message.Parameters.AddWithValue("id", messageId);
        message.Parameters.AddWithValue("thread", ticket.Id);
        message.Parameters.AddWithValue("body", ticket.Messages[0].Body);
        message.Parameters.AddWithValue("at", ticket.Messages[0].At);
        await message.ExecuteNonQueryAsync(ct);
    }

    public static string Write(SupportTicket ticket) => JsonSerializer.Serialize(new
    {
        id = ticket.Id,
        subject = ticket.Subject,
        messages = ticket.Messages.Select(line => new { author = line.Author, body = line.Body, at = line.At })
    }, Json);
}

sealed class SupportBag(string user, string subject)
{
    public string User { get; } = user;
    public string Subject { get; } = subject;
    public List<SupportLine> Lines { get; } = [];
}
