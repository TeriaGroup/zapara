using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Zapara.Contracts.Accounts;

namespace Zapara.Server.Accounts;

public sealed class RecoveryService(
    AccountsDataSource dataSource,
    AccountsConfiguration configuration,
    TimeProvider clock,
    IHostEnvironment environment,
    IRecoveryDelivery delivery,
    ILogger<RecoveryService> logger,
    AccountPasswordWork? passwords = null)
{
    private readonly string schema = configuration.QuotedSchema;
    private readonly AccountPasswordWork passwords = passwords ?? new();
    private readonly IRecoveryDelivery? channel =
        (environment.IsDevelopment() || environment.IsEnvironment("Testing")) && delivery is not UnconfiguredRecoveryDelivery
            ? delivery : null;

    public bool RecoveryEnabled => channel is not null;

    public Task StartEmailAsync(string accessToken, StartRecoveryEmailRequest request, CancellationToken ct = default)
    {
        Required(request);
        var sink = channel ?? throw new AccountServiceException(AccountFailure.InvalidRequest);
        var access = AccountTokens.Hash(accessToken, "za_");
        var proof = ProofHash(request.ProofToken);
        var token = RecoveryTokens.Create();
        var hash = RecoveryTokens.Hash(token);
        return DatabaseAsync(async db =>
        {
            await using var tx = await db.BeginAsync();
            var (user, family) = await db.AuthorizeAsync(access);
            await CheckProof(db, proof, user, family, ct);
            await db.ExecuteAsync($"UPDATE {schema}.reauth_proofs SET consumed_at=@p0 WHERE proof_hash=@p1", db.Now, proof);
            await db.ExecuteAsync($"""
                INSERT INTO {schema}.recovery_email_tokens(token_hash,user_id,email,expires_at)
                VALUES(@p0,@p1,@p2,@p3)
                """, hash, user.User.UserId, request.Email, db.Now.AddMinutes(30));
            await db.CommitAsync(tx);
            await sink.SendVerificationAsync(user.User.UserId, request.Email, token, ct);
            return true;
        }, ct);
    }

    public Task ConfirmEmailAsync(ConfirmRecoveryEmailRequest request, CancellationToken ct = default)
    {
        Required(request);
        var hash = RecoveryTokens.Hash(request.Token);
        return DatabaseAsync(async db =>
        {
            await using var tx = await db.BeginAsync();
            Guid userId;
            string email;
            await using (var command = db.Command($"""
                SELECT user_id,email,expires_at,consumed_at FROM {schema}.recovery_email_tokens
                WHERE token_hash=@p0 FOR UPDATE
                """, hash))
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct) || !reader.IsDBNull(3) || reader.GetFieldValue<DateTimeOffset>(2) <= db.Now)
                    throw new AccountServiceException(AccountFailure.InvalidRequest);
                userId = reader.GetGuid(0);
                email = reader.GetString(1);
            }
            await db.ExecuteAsync($"""
                INSERT INTO {schema}.recovery_addresses(user_id,email,verified_at) VALUES(@p0,@p1,@p2)
                ON CONFLICT (user_id) DO UPDATE SET email=EXCLUDED.email,verified_at=EXCLUDED.verified_at
                """, userId, email, db.Now);
            await db.ExecuteAsync($"UPDATE {schema}.recovery_email_tokens SET consumed_at=@p0 WHERE token_hash=@p1", db.Now, hash);
            await db.CommitAsync(tx);
            return true;
        }, ct);
    }

    public Task RequestResetAsync(PasswordResetRequest request, CancellationToken ct = default)
    {
        Required(request);
        string? normalized = null;
        try { normalized = AccountValidation.NormalizeUsername(request.Username); }
        catch (ArgumentException) { return Task.CompletedTask; }
        var sink = channel;
        if (sink is null || normalized is null) return Task.CompletedTask;
        var token = RecoveryTokens.Create();
        var hash = RecoveryTokens.Hash(token);
        return DatabaseAsync(async db =>
        {
            var snapshot = await db.UserAsync(username: normalized);
            if (snapshot is null || snapshot.Status != "active") return true;
            await using var tx = await db.BeginAsync();
            var current = await db.UserAsync(snapshot.User.UserId, locked: true);
            if (current is null || current.Status != "active" || await db.CredentialAsync(current.User.UserId) is null)
            {
                await db.CommitAsync(tx);
                return true;
            }
            string? email;
            await using (var command = db.Command($"SELECT email FROM {schema}.recovery_addresses WHERE user_id=@p0", current.User.UserId))
            await using (var reader = await command.ExecuteReaderAsync(ct))
                email = await reader.ReadAsync(ct) ? reader.GetString(0) : null;
            if (email is null)
            {
                await db.CommitAsync(tx);
                return true;
            }
            await db.ExecuteAsync($"""
                INSERT INTO {schema}.password_reset_tokens(token_hash,user_id,expires_at) VALUES(@p0,@p1,@p2)
                """, hash, current.User.UserId, db.Now.AddMinutes(15));
            await db.CommitAsync(tx);
            try { await sink.SendResetAsync(email, token, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                logger.LogWarning("Доставка сброса пароля недоступна.");
            }
            return true;
        }, ct);
    }

    public Task ConfirmResetAsync(PasswordResetConfirmRequest request, CancellationToken ct = default)
    {
        Required(request);
        Validate(() => AccountValidation.Password(request.NewPassword));
        var hash = RecoveryTokens.Hash(request.Token);
        return DatabaseAsync(async db =>
        {
            await using var tx = await db.BeginAsync();
            Guid userId;
            await using (var command = db.Command($"""
                SELECT user_id,expires_at,consumed_at FROM {schema}.password_reset_tokens
                WHERE token_hash=@p0 FOR UPDATE
                """, hash))
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct) || !reader.IsDBNull(2) || reader.GetFieldValue<DateTimeOffset>(1) <= db.Now)
                    throw new AccountServiceException(AccountFailure.InvalidRequest);
                userId = reader.GetGuid(0);
            }
            if (await db.UserAsync(userId, locked: true) is null)
                throw new AccountServiceException(AccountFailure.InvalidRequest);
            await db.ExecuteAsync($"SELECT family_id FROM {schema}.session_families WHERE user_id=@p0 ORDER BY family_id FOR UPDATE", userId);
            if (await db.CredentialAsync(userId, true) is null)
                throw new AccountServiceException(AccountFailure.InvalidRequest);
            var password = passwords.Hash(userId, request.NewPassword, ct);
            await db.ExecuteAsync($"""
                UPDATE {schema}.users SET credential_version=credential_version+1 WHERE user_id=@p0;
                UPDATE {schema}.password_credentials SET password_hash=@p1,changed_at=@p2,failed_count=0,
                failure_window_started_at=NULL,locked_until=NULL WHERE user_id=@p0
                """, userId, password, db.Now);
            await db.ExecuteAsync($"UPDATE {schema}.password_reset_tokens SET consumed_at=@p0 WHERE user_id=@p1 AND consumed_at IS NULL", db.Now, userId);
            await db.RevokeAsync(userId, null, "password_change");
            await db.AuditAsync(userId, null, "password_reset");
            await db.CommitAsync(tx);
            return true;
        }, ct);
    }

    private async Task CheckProof(AccountRepository db, byte[] hash, AccountRow user, FamilyRow family, CancellationToken ct)
    {
        await using var command = db.Command($"""
            SELECT user_id,family_id,purpose,security_version,expires_at,consumed_at,reservation
            FROM {schema}.reauth_proofs WHERE proof_hash=@p0 FOR UPDATE
            """, hash);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.GetGuid(0) != user.User.UserId || reader.GetGuid(1) != family.Id ||
            reader.GetString(2) != "set_recovery_email" || reader.GetInt64(3) != user.Version ||
            reader.GetFieldValue<DateTimeOffset>(4) <= db.Now || !reader.IsDBNull(5) || !reader.IsDBNull(6))
            throw ExternalAuthException.Invalid();
    }

    private static byte[] ProofHash(string proof)
    {
        ExternalSecrets.Token(proof, 43, 43);
        return ExternalSecrets.Hash(proof);
    }

    private static void Required(object? request)
    {
        if (request is null) throw new AccountServiceException(AccountFailure.InvalidRequest);
    }

    private static T Validate<T>(Func<T> validate)
    {
        try { return validate(); }
        catch (ArgumentException) { throw new AccountServiceException(AccountFailure.InvalidRequest); }
    }

    private async Task<T> DatabaseAsync<T>(Func<AccountRepository, Task<T>> operation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await using var connection = dataSource.CreateConnection();
            await connection.OpenAsync(ct);
            return await operation(new(connection, schema, clock, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            throw new AccountServiceException(AccountFailure.DbUnavailable);
        }
    }
}
