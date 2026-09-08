using System.Text;
using Npgsql;
using Zapara.Contracts.Sync;
using Zapara.Server.Accounts;

namespace Zapara.Server.Sync;

// Callback scoped: never owns a connection/transaction or performs commit/retry.
internal sealed partial class SyncRepository(TrustedAccountContext context, SyncConfiguration configuration, CancellationToken ct)
{
    private string Schema => configuration.QuotedSchema;
    private Guid UserId => context.UserId;
    // PostgreSQL timestamps have microsecond precision; feed, receipts and snapshots must agree.
    private DateTimeOffset Now => new(context.UtcNow.Ticks - context.UtcNow.Ticks % 10, TimeSpan.Zero);
    private NpgsqlCommand Command(string sql, params object[] parameters)
    {
        var command = new NpgsqlCommand(sql, context.Connection, context.Transaction);
        for (var i = 0; i < parameters.Length; i++) command.Parameters.AddWithValue("p" + i, parameters[i]);
        return command;
    }
    private async Task ExecuteAsync(string sql, params object[] parameters)
    {
        await using var command = Command(sql, parameters);
        await command.ExecuteNonQueryAsync(ct);
    }
    private async Task<T> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var command = Command(sql, parameters);
        return (T)(await command.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Отсутствует состояние Sync."));
    }
    private static T Parse<T>(string json) => SyncJson.Parse<T>(Encoding.UTF8.GetBytes(json));
    private static string Json<T>(T value) => Encoding.UTF8.GetString(SyncJson.Serialize(value));

    internal async Task<SyncMetadata> InitializeAsync()
    {
        await ExecuteAsync($"""
            INSERT INTO {Schema}.sync_state(user_id,epoch,sequence,min_after_sequence,last_maintenance_at)
            VALUES(@p0,@p1,0,0,@p2) ON CONFLICT(user_id) DO NOTHING
            """, UserId, Guid.NewGuid(), Now);
        SyncMetadata metadata;
        DateTimeOffset lastMaintenance;
        await using (var command = Command($"SELECT epoch,sequence,min_after_sequence,last_maintenance_at FROM {Schema}.sync_state WHERE user_id=@p0 FOR UPDATE", UserId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Отсутствует состояние Sync.");
            metadata = new(reader.GetGuid(0), reader.GetInt64(1), reader.GetInt64(2));
            lastMaintenance = reader.GetFieldValue<DateTimeOffset>(3);
        }
        return Now - lastMaintenance >= TimeSpan.FromDays(1) ? await MaintainAsync(metadata) : metadata;
    }
}
