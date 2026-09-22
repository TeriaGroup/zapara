using System.Text.Json.Nodes;
using Zapara.Server.Accounts;

namespace Zapara.Server.Social;

internal sealed class SocialLifecycleParticipant(SocialConfiguration configuration) : IAccountLifecycleParticipant
{
    public string Module => "social";

    public async Task ContributeExportAsync(AccountLifecycleContext context, CancellationToken ct)
    {
        if (!await ReadyAsync(context, ct)) return;
        await using var command = context.Command($"""
            SELECT m.message_id, m.kind, m.body, a.original_name, m.created_at
            FROM {configuration.QuotedSchema}.messages m
            LEFT JOIN {configuration.QuotedSchema}.attachments a ON a.message_id=m.message_id
            WHERE m.sender_id=@p0
            ORDER BY m.message_no
            """, context.UserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            context.Export.Contributions.Add(new JsonObject
            {
                ["type"] = "direct_message",
                ["id"] = AccountExportDocument.Id(reader.GetGuid(0)),
                ["kind"] = reader.GetString(1),
                ["body"] = reader.IsDBNull(2) ? null : reader.GetString(2),
                ["fileName"] = reader.IsDBNull(3) ? null : reader.GetString(3),
                ["createdAt"] = AccountExportDocument.Utc(reader.GetFieldValue<DateTimeOffset>(4))
            });
    }

    public async Task DeleteOwnedDataAsync(AccountLifecycleContext context, CancellationToken ct)
    {
        if (!await ReadyAsync(context, ct)) return;
        var schema = configuration.QuotedSchema;
        await context.ExecuteAsync($"""
            INSERT INTO {schema}.file_purge(stored_name, created_at)
            SELECT a.stored_name, @p1 FROM {schema}.attachments a
            JOIN {schema}.messages m ON m.message_id=a.message_id
            JOIN {schema}.conversations c ON c.conversation_id=m.conversation_id
            JOIN {schema}.friendships f ON f.friendship_id=c.friendship_id
            WHERE f.requester_id=@p0 OR f.addressee_id=@p0
            ON CONFLICT (stored_name) DO NOTHING
            """, context.UserId, context.UtcNow);
        await context.ExecuteAsync($"DELETE FROM {schema}.friendships WHERE requester_id=@p0 OR addressee_id=@p0", context.UserId);
        await context.ExecuteAsync($"DELETE FROM {schema}.codes WHERE user_id=@p0", context.UserId);
    }

    private async Task<bool> ReadyAsync(AccountLifecycleContext context, CancellationToken ct)
    {
        await using var command = context.Command("SELECT to_regclass(@p0)::text", configuration.Schema + ".messages");
        return await command.ExecuteScalarAsync(ct) is string name && name.Length > 0;
    }
}
