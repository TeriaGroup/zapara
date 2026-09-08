using System.Text.Json.Nodes;
using Zapara.Server.Accounts;

namespace Zapara.Server.Sync;

public sealed class SyncLifecycleParticipant(SyncConfiguration configuration) : IAccountLifecycleParticipant
{
    private readonly string schema = configuration.QuotedSchema;
    public string Module => "sync";

    public async Task ContributeExportAsync(AccountLifecycleContext context, CancellationToken ct)
    {
        await using var command = context.Command($"""
            SELECT entity_type,entity_id,revision,tombstone,changed_at,payload::text
            FROM {schema}.sync_records WHERE user_id=@p0 ORDER BY entity_type, entity_id
            """, context.UserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var item = new JsonObject
            {
                ["entityType"] = reader.GetString(0),
                ["entityId"] = AccountExportDocument.Id(reader.GetGuid(1)),
                ["revision"] = reader.GetInt64(2),
                ["tombstone"] = reader.GetBoolean(3),
                ["changedAt"] = AccountExportDocument.Utc(reader.GetFieldValue<DateTimeOffset>(4)),
                ["payload"] = reader.IsDBNull(5) ? null : JsonNode.Parse(reader.GetString(5))
            };
            context.Export.PrivateSync.Add(item);
        }
    }

    public Task DeleteOwnedDataAsync(AccountLifecycleContext context, CancellationToken ct)
        => context.ExecuteAsync($"DELETE FROM {schema}.sync_state WHERE user_id=@p0", context.UserId);
}
