using Npgsql;
using NpgsqlTypes;
using Zapara.Contracts.Accounts;

namespace Zapara.Server.Accounts;

internal sealed partial class AccountRepository(NpgsqlConnection connection, string schema, TimeProvider clock, CancellationToken ct)
{
    internal NpgsqlConnection Connection => connection;
    internal DateTimeOffset Now => clock.GetUtcNow();
    internal Task<NpgsqlTransaction> BeginAsync() => connection.BeginTransactionAsync(ct).AsTask();

    internal NpgsqlCommand Command(string sql, params object?[] values)
    {
        var command = new NpgsqlCommand(sql, connection);
        for (var i = 0; i < values.Length; i++)
        {
            var parameter = new NpgsqlParameter { ParameterName = "p" + i, Value = values[i] ?? DBNull.Value };
            if (values[i] is null) parameter.NpgsqlDbType = NpgsqlDbType.Text;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    internal async Task ExecuteAsync(string sql, params object?[] values)
    {
        await using var command = Command(sql, values);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal async Task<AccountRow?> UserAsync(Guid? id = null, string? username = null, bool locked = false)
    {
        await using var command = Command($"""
            SELECT user_id,username,display_name,created_at,status,credential_version FROM {schema}.users
            WHERE {(id.HasValue ? "user_id=@p0" : "normalized_username=@p0")} {(locked ? "FOR UPDATE" : "")}
            """, id.HasValue ? id.Value : username);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(new(reader.GetGuid(0), reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)),
            reader.GetString(4), reader.GetInt64(5)) : null;
    }

    internal async Task<CredentialRow?> CredentialAsync(Guid userId, bool locked = false)
    {
        await using var command = Command($"""
            SELECT password_hash,failed_count,failure_window_started_at,locked_until
            FROM {schema}.password_credentials WHERE user_id=@p0 {(locked ? "FOR UPDATE" : "")}
            """, userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetString(0), reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3)) : null;
    }

    internal Task AuditAsync(Guid userId, Guid? familyId, string action, string outcome = "success")
        => ExecuteAsync($"""
            INSERT INTO {schema}.account_security_events(event_id,user_id,family_id,action,outcome,created_at)
            VALUES(@p0,@p1,CAST(@p2 AS uuid),@p3,@p4,@p5)
            """, Guid.NewGuid(), userId, familyId?.ToString(), action, outcome, Now);

    internal async Task CommitAsync(NpgsqlTransaction transaction)
    {
        ct.ThrowIfCancellationRequested();
        try { await transaction.CommitAsync(ct); }
        catch (Exception e) when (e is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            // Once commit starts its outcome can be uncertain. Never retry or report success.
            throw new AccountServiceException(AccountFailure.DbUnavailable);
        }
    }
}

internal sealed record AccountRow(UserResponse User, string Status, long Version);
internal sealed record CredentialRow(string Hash, int Failures, DateTimeOffset? Window, DateTimeOffset? LockedUntil)
{
    public override string ToString() => "CredentialRow { [REDACTED] }";
}
