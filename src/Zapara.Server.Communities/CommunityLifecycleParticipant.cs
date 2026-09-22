using System.Text.Json.Nodes;
using Zapara.Server.Accounts;

namespace Zapara.Server.Communities;

internal sealed class CommunityLifecycleParticipant(CommunitiesConfiguration configuration) : IAccountLifecycleParticipant
{
    private readonly string schema = configuration.QuotedSchema;
    public string Module => "communities";

    public async Task ContributeExportAsync(AccountLifecycleContext context, CancellationToken ct)
    {
        await using (var command = context.Command($"""
            SELECT community_id,role,status FROM {schema}.memberships WHERE user_id=@p0 ORDER BY community_id
            """, context.UserId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                context.Export.Memberships.Add(new JsonObject
                {
                    ["communityId"] = AccountExportDocument.Id(reader.GetGuid(0)),
                    ["role"] = reader.GetString(1),
                    ["status"] = reader.GetString(2)
                });
        }
        await using (var command = context.Command($"""
            SELECT homework_id,community_id,title,body,revision,created_at FROM {schema}.shared_homework
            WHERE created_by=@p0 ORDER BY created_at, homework_id
            """, context.UserId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                context.Export.Contributions.Add(Contribution("homework", reader));
        }
        await using (var command = context.Command($"""
            SELECT announcement_id,community_id,title,body,revision,created_at FROM {schema}.announcements
            WHERE created_by=@p0 ORDER BY created_at, announcement_id
            """, context.UserId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                context.Export.Contributions.Add(Contribution("announcement", reader));
        }
        await using (var command = context.Command($"""
            SELECT poll_id,community_id,question,revision,created_at FROM {schema}.polls
            WHERE created_by=@p0 ORDER BY created_at, poll_id
            """, context.UserId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                context.Export.Contributions.Add(new JsonObject
                {
                    ["type"] = "poll",
                    ["id"] = AccountExportDocument.Id(reader.GetGuid(0)),
                    ["communityId"] = AccountExportDocument.Id(reader.GetGuid(1)),
                    ["question"] = reader.GetString(2),
                    ["revision"] = reader.GetInt64(3),
                    ["createdAt"] = AccountExportDocument.Utc(reader.GetFieldValue<DateTimeOffset>(4))
                });
        }
        await using (var command = context.Command($"""
            SELECT homework_id,completed,revision FROM {schema}.shared_homework_completion
            WHERE user_id=@p0 ORDER BY homework_id
            """, context.UserId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                context.Export.Completions.Add(new JsonObject
                {
                    ["homeworkId"] = AccountExportDocument.Id(reader.GetGuid(0)),
                    ["completed"] = reader.GetBoolean(1),
                    ["revision"] = reader.GetInt64(2)
                });
        }
        if (await MessagesTableAsync(context, "chat_messages", ct))
        {
            await using var command = context.Command($"""
                SELECT m.message_id, c.community_id, m.body, m.created_at
                FROM {configuration.QuotedMessages}.chat_messages m
                JOIN {configuration.QuotedMessages}.conversations c ON c.conversation_id=m.conversation_id
                WHERE m.sender_id=@p0
                ORDER BY m.message_no
                """, context.UserId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                context.Export.Contributions.Add(new JsonObject
                {
                    ["type"] = "message",
                    ["id"] = AccountExportDocument.Id(reader.GetGuid(0)),
                    ["communityId"] = AccountExportDocument.Id(reader.GetGuid(1)),
                    ["body"] = reader.GetString(2),
                    ["createdAt"] = AccountExportDocument.Utc(reader.GetFieldValue<DateTimeOffset>(3))
                });
        }
        await using (var command = context.Command($"""
            SELECT poll_id,option_id,created_at FROM {schema}.votes WHERE user_id=@p0 ORDER BY poll_id
            """, context.UserId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                context.Export.Votes.Add(new JsonObject
                {
                    ["pollId"] = AccountExportDocument.Id(reader.GetGuid(0)),
                    ["optionId"] = AccountExportDocument.Id(reader.GetGuid(1)),
                    ["createdAt"] = AccountExportDocument.Utc(reader.GetFieldValue<DateTimeOffset>(2))
                });
        }
    }

    public async Task DeleteOwnedDataAsync(AccountLifecycleContext context, CancellationToken ct)
    {
        if (await MessagesTableAsync(context, "chat_messages", ct))
            await context.ExecuteAsync($"DELETE FROM {configuration.QuotedMessages}.chat_messages WHERE sender_id=@p0", context.UserId);
        if (await MessagesTableAsync(context, "conversation_members", ct))
            await context.ExecuteAsync($"DELETE FROM {configuration.QuotedMessages}.conversation_members WHERE user_id=@p0", context.UserId);
        var user = context.UserId;
        await context.ExecuteAsync($"DELETE FROM {schema}.votes WHERE user_id=@p0", user);
        await context.ExecuteAsync($"DELETE FROM {schema}.shared_homework_completion WHERE user_id=@p0", user);
        await context.ExecuteAsync($"UPDATE {schema}.join_requests SET resolved_by=NULL WHERE resolved_by=@p0", user);
        await context.ExecuteAsync($"DELETE FROM {schema}.join_requests WHERE user_id=@p0", user);
        await context.ExecuteAsync($"DELETE FROM {schema}.staff_assignments WHERE user_id=@p0", user);
        await context.ExecuteAsync($"DELETE FROM {schema}.memberships WHERE user_id=@p0", user);
        await context.ExecuteAsync($"UPDATE {schema}.shared_homework SET created_by=NULL WHERE created_by=@p0", user);
        await context.ExecuteAsync($"UPDATE {schema}.announcements SET created_by=NULL WHERE created_by=@p0", user);
        await context.ExecuteAsync($"UPDATE {schema}.polls SET created_by=NULL WHERE created_by=@p0", user);
    }

    private async Task<bool> MessagesTableAsync(AccountLifecycleContext context, string table, CancellationToken ct)
    {
        await using var command = context.Command("SELECT to_regclass(@p0) IS NOT NULL", configuration.MessagesSchema + "." + table);
        return await command.ExecuteScalarAsync(ct) is true;
    }

    private static JsonObject Contribution(string type, Npgsql.NpgsqlDataReader reader) => new()
    {
        ["type"] = type,
        ["id"] = AccountExportDocument.Id(reader.GetGuid(0)),
        ["communityId"] = AccountExportDocument.Id(reader.GetGuid(1)),
        ["title"] = reader.GetString(2),
        ["body"] = reader.GetString(3),
        ["revision"] = reader.GetInt64(4),
        ["createdAt"] = AccountExportDocument.Utc(reader.GetFieldValue<DateTimeOffset>(5))
    };
}
