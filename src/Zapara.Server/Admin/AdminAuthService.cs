using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Npgsql;
using Zapara.Contracts.Accounts;
using Zapara.Server.Accounts;

namespace Zapara.Server.Admin;

public sealed class AdminAuthService(AccountsDataSource dataSource, AdminConfiguration configuration, TimeProvider clock)
{
    private readonly IPasswordHasher<AccountUser> hasher = new PasswordHasher<AccountUser>(Options.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = 100000
    }));
    private readonly Lazy<string> dummy = new(() => new PasswordHasher<AccountUser>(Options.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = 100000
    })).HashPassword(new(Guid.Empty), "dummy credential not an account password"));

    public async Task<AdminIdentity> AuthenticateAsync(string username, string password, CancellationToken ct)
    {
        string normalized;
        try { normalized = AccountValidation.NormalizeUsername(username); AccountValidation.Password(password); }
        catch (ArgumentException) { throw AdminException.Unauthorized(); }
        try
        {
            await using var connection = dataSource.CreateConnection();
            await connection.OpenAsync(ct);
            Guid userId;
            string status;
            long version;
            string? hash;
            int failures;
            DateTimeOffset? window;
            DateTimeOffset? locked;
            await using (var lookup = new NpgsqlCommand($"""
                SELECT u.user_id,u.status,u.credential_version,c.password_hash,c.failed_count,c.failure_window_started_at,c.locked_until
                FROM {configuration.Accounts.QuotedSchema}.users u
                LEFT JOIN {configuration.Accounts.QuotedSchema}.password_credentials c ON c.user_id=u.user_id
                WHERE u.normalized_username=@name
                """, connection))
            {
                lookup.Parameters.AddWithValue("name", normalized);
                await using var reader = await lookup.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                {
                    hasher.VerifyHashedPassword(new(Guid.Empty), dummy.Value, password);
                    throw AdminException.Unauthorized();
                }
                userId = reader.GetGuid(0);
                status = reader.GetString(1);
                version = reader.GetInt64(2);
                hash = reader.IsDBNull(3) ? null : reader.GetString(3);
                failures = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
                window = reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5);
                locked = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6);
            }
            var now = clock.GetUtcNow().ToUniversalTime();
            if (status != "active" || hash is null || locked > now)
            {
                hasher.VerifyHashedPassword(new(Guid.Empty), dummy.Value, password);
                throw AdminException.Unauthorized();
            }
            var result = hasher.VerifyHashedPassword(new(userId), hash, password);
            await using var tx = await connection.BeginTransactionAsync(ct);
            string currentStatus;
            long currentVersion;
            string currentHash;
            int currentFailures;
            DateTimeOffset? currentWindow;
            DateTimeOffset? currentLocked;
            await using (var lockedUser = new NpgsqlCommand($"""
                SELECT u.status,u.credential_version,c.password_hash,c.failed_count,c.failure_window_started_at,c.locked_until
                FROM {configuration.Accounts.QuotedSchema}.users u
                JOIN {configuration.Accounts.QuotedSchema}.password_credentials c ON c.user_id=u.user_id
                WHERE u.user_id=@id FOR UPDATE
                """, connection, tx))
            {
                lockedUser.Parameters.AddWithValue("id", userId);
                await using var reader = await lockedUser.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw AdminException.Unauthorized();
                currentStatus = reader.GetString(0);
                currentVersion = reader.GetInt64(1);
                currentHash = reader.GetString(2);
                currentFailures = reader.GetInt32(3);
                currentWindow = reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4);
                currentLocked = reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5);
            }
            if (currentStatus != "active" || currentVersion != version || currentHash != hash || currentLocked > now)
                throw AdminException.Unauthorized();
            if (result == PasswordVerificationResult.Failed)
            {
                var reset = currentWindow is null || now >= currentWindow.Value.AddMinutes(15);
                var count = reset ? 1 : Math.Min(5, currentFailures + 1);
                var started = reset ? now : currentWindow!.Value;
                await using var fail = new NpgsqlCommand($"""
                    UPDATE {configuration.Accounts.QuotedSchema}.password_credentials
                    SET failed_count=@count,failure_window_started_at=@window,locked_until=CASE WHEN @count>=5 THEN @until ELSE NULL END
                    WHERE user_id=@id
                    """, connection, tx);
                fail.Parameters.AddWithValue("count", count);
                fail.Parameters.AddWithValue("window", started);
                fail.Parameters.AddWithValue("until", now.AddMinutes(15));
                fail.Parameters.AddWithValue("id", userId);
                await fail.ExecuteNonQueryAsync(ct);
                await Audit(connection, tx, userId, "login", "admin_session", userId.ToString("D"), "denied", now, ct);
                await tx.CommitAsync(ct);
                throw AdminException.Unauthorized();
            }
            await using (var admin = new NpgsqlCommand($"""
                SELECT 1 FROM {configuration.QuotedSchema}.platform_admins WHERE user_id=@id AND revoked_at IS NULL
                """, connection, tx))
            {
                admin.Parameters.AddWithValue("id", userId);
                if (await admin.ExecuteScalarAsync(ct) is null)
                {
                    await Audit(connection, tx, userId, "login", "admin_session", userId.ToString("D"), "denied", now, ct);
                    await tx.CommitAsync(ct);
                    throw AdminException.Unauthorized();
                }
            }
            await using (var clear = new NpgsqlCommand($"""
                UPDATE {configuration.Accounts.QuotedSchema}.password_credentials
                SET failed_count=0,failure_window_started_at=NULL,locked_until=NULL WHERE user_id=@id
                """, connection, tx))
            {
                clear.Parameters.AddWithValue("id", userId);
                await clear.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            return new AdminIdentity(userId, currentVersion);
        }
        catch (AdminException) { throw; }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            throw AdminException.Unavailable();
        }
    }

    public async Task VerifyPasswordAsync(Guid userId, string password, long expectedVersion, CancellationToken ct)
    {
        try { AccountValidation.Password(password); }
        catch (ArgumentException) { throw AdminException.Reauth(); }
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"""
            SELECT u.status,u.credential_version,c.password_hash
            FROM {configuration.Accounts.QuotedSchema}.users u
            JOIN {configuration.Accounts.QuotedSchema}.password_credentials c ON c.user_id=u.user_id
            WHERE u.user_id=@id
            """, connection);
        command.Parameters.AddWithValue("id", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.GetString(0) != "active" || reader.GetInt64(1) != expectedVersion)
            throw AdminException.Reauth();
        var hash = reader.GetString(2);
        if (hasher.VerifyHashedPassword(new(userId), hash, password) == PasswordVerificationResult.Failed)
            throw AdminException.Reauth();
    }

    private async Task Audit(NpgsqlConnection connection, NpgsqlTransaction tx, Guid? actor, string action, string objectType,
        string objectId, string outcome, DateTimeOffset now, CancellationToken ct)
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
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(ct);
    }
}

public sealed record AdminIdentity(Guid UserId, long CredentialVersion);
