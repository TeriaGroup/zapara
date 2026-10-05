using Npgsql;
using Zapara.Server.Accounts;

namespace Zapara.Server.Admin;

public enum AdminBootstrapOutcome { Committed, Refused, Failed }

public static class AdminBootstrap
{
    public static async Task<AdminBootstrapOutcome> RunAsync(AccountsDataSource dataSource, AdminConfiguration configuration,
        Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) return AdminBootstrapOutcome.Failed;
        try
        {
            await using var connection = dataSource.CreateMigrationConnection();
            await connection.OpenAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(ct);
            await using (var zone = new NpgsqlCommand("SET LOCAL search_path = pg_catalog; SET LOCAL TIME ZONE 'UTC'", connection, tx))
                await zone.ExecuteNonQueryAsync(ct);
            await using (var lockTable = new NpgsqlCommand($"LOCK TABLE {configuration.QuotedSchema}.platform_admins IN SHARE ROW EXCLUSIVE MODE", connection, tx))
                await lockTable.ExecuteNonQueryAsync(ct);
            await using (var existing = new NpgsqlCommand($"SELECT user_id FROM {configuration.QuotedSchema}.platform_admins LIMIT 1", connection, tx))
            {
                if (await existing.ExecuteScalarAsync(ct) is not null and not DBNull)
                {
                    await Audit(connection, tx, configuration, null, "bootstrap", "platform_admin", userId.ToString("D"), "denied", ct);
                    await tx.CommitAsync(ct);
                    return AdminBootstrapOutcome.Refused;
                }
            }
            await using (var user = new NpgsqlCommand($"""
                SELECT status FROM {configuration.Accounts.QuotedSchema}.users WHERE user_id=@id FOR UPDATE
                """, connection, tx))
            {
                user.Parameters.AddWithValue("id", userId);
                var status = await user.ExecuteScalarAsync(ct) as string;
                if (status != "active")
                {
                    await tx.RollbackAsync(ct);
                    return AdminBootstrapOutcome.Failed;
                }
            }
            var now = DateTimeOffset.UtcNow;
            await using (var insert = new NpgsqlCommand($"""
                INSERT INTO {configuration.QuotedSchema}.platform_admins(user_id,granted_at,revoked_at)
                VALUES(@id,@now,NULL)
                """, connection, tx))
            {
                insert.Parameters.AddWithValue("id", userId);
                insert.Parameters.AddWithValue("now", now);
                await insert.ExecuteNonQueryAsync(ct);
            }
            await Audit(connection, tx, configuration, userId, "bootstrap", "platform_admin", userId.ToString("D"), "success", ct);
            await tx.CommitAsync(ct);
            return AdminBootstrapOutcome.Committed;
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException or ArgumentException)
        {
            return AdminBootstrapOutcome.Failed;
        }
    }

    private static async Task Audit(NpgsqlConnection connection, NpgsqlTransaction tx, AdminConfiguration configuration,
        Guid? actor, string action, string objectType, string objectId, string outcome, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {configuration.QuotedSchema}.admin_audit(event_id,actor_id,action,object_type,object_id,outcome,created_at)
            VALUES(@id,@actor,@action,@type,@object,@outcome,@now)
            """, connection, tx);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("actor", actor.HasValue ? actor.Value : DBNull.Value);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("type", objectType);
        command.Parameters.AddWithValue("object", objectId);
        command.Parameters.AddWithValue("outcome", outcome);
        command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(ct);
    }
}
