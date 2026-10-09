using Microsoft.AspNetCore.Identity;
using Npgsql;
using Zapara.Contracts.Accounts;

namespace Zapara.Server.Accounts;

public sealed partial class AccountService
{
    public Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        Required(request);
        var normalized = Validate(() => AccountValidation.NormalizeUsername(request.Username));
        Validate(() => AccountValidation.Password(request.Password));
        Validate(() => AccountValidation.DisplayName(request.DisplayName));
        var id = Guid.NewGuid();
        var hash = passwords.Hash(id, request.Password, ct);
        return DatabaseAsync(async db =>
        {
            await using var tx = await db.BeginAsync();
            var now = db.Now;
            try
            {
                await db.ExecuteAsync($"""
                    INSERT INTO {schema}.users(user_id,username,normalized_username,display_name,created_at,status)
                    VALUES(@p0,@p1,@p2,@p3,@p4,'active')
                    """, id, request.Username, normalized, request.DisplayName, now);
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation &&
                e.ConstraintName == "users_normalized_username_key")
            {
                throw new AccountServiceException(AccountFailure.UsernameUnavailable);
            }
            await db.ExecuteAsync($"""
                INSERT INTO {schema}.password_credentials(user_id,password_hash,changed_at) VALUES(@p0,@p1,@p2)
                """, id, hash, now);
            await db.AuditAsync(id, null, "register");
            await db.CommitAsync(tx);
            return new UserResponse(id, request.Username, request.DisplayName, now);
        }, ct);
    }

    /// <summary>Login from an unknown network (in-process callers and tests share one throttling key).</summary>
    public Task<SessionResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        => LoginAsync(request, LoginThrottle.NetworkKey(null), ct);

    /// <param name="network">Client network from <see cref="LoginThrottle.NetworkKey"/>.</param>
    public async Task<SessionResponse> LoginAsync(LoginRequest request, string network, CancellationToken ct = default)
    {
        Required(request);
        ArgumentException.ThrowIfNullOrEmpty(network);
        var normalized = Validate(() => AccountValidation.NormalizeUsername(request.Username));
        Validate(() => AccountValidation.Password(request.Password));
        Validate(() => AccountValidation.Id(request.Device.DeviceId));
        Validate(() => AccountValidation.DeviceName(request.Device.DeviceName));
        Validate(() => AccountValidation.Platform(request.Device.Platform));
        // Checked before the password: a throttled attempt never learns whether the password was right.
        var decision = throttle.Evaluate(normalized, network);
        // A device that has logged in to this account before skips network blocks (so a shared or attacked network
        // does not lock its owner out), but never the per-account cap.
        if (decision.Block == LoginBlock.Account ||
            (decision.Block == LoginBlock.Network && !await KnownDeviceAsync(normalized, request.Device.DeviceId, ct)))
            throw new AccountServiceException(AccountFailure.RateLimited) { RetryAfter = decision.Wait };
        try
        {
            var session = await PasswordLoginAsync(request, normalized, ct);
            throttle.Succeeded(normalized, network);
            return session;
        }
        catch (AccountServiceException e) when (e.Failure == AccountFailure.InvalidCredentials)
        {
            // Unknown usernames count too, so probing names costs the same as guessing passwords.
            throttle.Failed(normalized, network);
            throw;
        }
    }

    /// <summary>How long a device stays "known" for throttling after it was last used with the account.</summary>
    internal static readonly TimeSpan KnownDeviceLifetime = TimeSpan.FromDays(90);

    /// <summary>True when this device id has an earlier session for this account, seen within <see cref="KnownDeviceLifetime"/>.
    /// Device ids are random per installation and never shown to other users.</summary>
    private Task<bool> KnownDeviceAsync(string normalized, Guid deviceId, CancellationToken ct)
        => DatabaseAsync(async db =>
        {
            await using var command = db.Command($"""
                SELECT EXISTS(SELECT 1 FROM {schema}.session_families f JOIN {schema}.users u ON u.user_id=f.user_id
                WHERE u.normalized_username=@p0 AND u.status='active' AND f.device_id=@p1 AND f.last_seen_at>@p2)
                """, normalized, deviceId, db.Now - KnownDeviceLifetime);
            return await command.ExecuteScalarAsync(ct) is true;
        }, ct);

    private Task<SessionResponse> PasswordLoginAsync(LoginRequest request, string normalized, CancellationToken ct)
    {
        return DatabaseAsync(async db =>
        {
            var snapshot = await db.UserAsync(username: normalized);
            var credential = snapshot is null ? null : await db.CredentialAsync(snapshot.User.UserId);
            if (snapshot is null || snapshot.Status != "active" || credential is null)
            {
                passwords.Dummy(request.Password, ct);
                throw InvalidCredentials();
            }
            var result = passwords.Verify(snapshot.User.UserId, credential.Hash, request.Password, ct);
            var rehash = result == PasswordVerificationResult.SuccessRehashNeeded
                ? passwords.Hash(snapshot.User.UserId, request.Password, ct) : null;
            await using var tx = await db.BeginAsync();
            var current = await db.UserAsync(snapshot.User.UserId, locked: true);
            var currentCredential = await db.CredentialAsync(snapshot.User.UserId, true);
            if (current is null || current.Status != "active" || current.Version != snapshot.Version ||
                currentCredential is null || currentCredential.Hash != credential.Hash)
                throw InvalidCredentials();
            if (result == PasswordVerificationResult.Failed)
            {
                await FailedLoginAsync(db, current.User.UserId, currentCredential);
                await db.CommitAsync(tx);
                throw InvalidCredentials();
            }
            await db.ExecuteAsync($"""
                UPDATE {schema}.password_credentials SET failed_count=0,failure_window_started_at=NULL,locked_until=NULL,
                password_hash=COALESCE(@p0,password_hash) WHERE user_id=@p1
                """, rehash, current.User.UserId);
            var familyId = Guid.NewGuid();
            var now = db.Now;
            var expiry = now.AddDays(30);
            await db.ExecuteAsync($"""
                INSERT INTO {schema}.session_families(family_id,user_id,device_id,device_name,platform,created_at,authenticated_at,last_seen_at,expires_at)
                VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p5,@p5,@p6)
                """, familyId, current.User.UserId, request.Device.DeviceId, request.Device.DeviceName, request.Device.Platform, now, expiry);
            var session = await db.IssueAsync(current.User, familyId, expiry);
            await db.AuditAsync(current.User.UserId, familyId, "login");
            await db.CommitAsync(tx);
            return session;
        }, ct);
    }

    /// <summary>The counters stay for audit and support screens. They no longer lock the account:
    /// throttling lives in <see cref="LoginThrottle"/>, keyed by client network.</summary>
    private async Task FailedLoginAsync(AccountRepository db, Guid userId, CredentialRow credential)
    {
        var now = db.Now;
        var reset = credential.Window is null || now >= credential.Window.Value.AddMinutes(15);
        var count = reset ? 1 : Math.Min(int.MaxValue - 1, credential.Failures) + 1;
        var window = reset ? now : credential.Window!.Value;
        await db.ExecuteAsync($"""
            UPDATE {schema}.password_credentials SET failed_count=@p0,failure_window_started_at=@p1,locked_until=NULL
            WHERE user_id=@p2
            """, count, window, userId);
        await db.AuditAsync(userId, null, "login", "invalid_credentials");
    }

    public Task ChangePasswordAsync(string accessToken, ChangePasswordRequest request, CancellationToken ct = default)
    {
        Required(request);
        Validate(() => AccountValidation.Password(request.CurrentPassword));
        Validate(() => AccountValidation.Password(request.NewPassword));
        var accessHash = AccountTokens.Hash(accessToken, "za_");
        return DatabaseAsync(async db =>
        {
            AccountRow snapshot;
            CredentialRow credential;
            await using (var read = await db.BeginAsync())
            {
                (snapshot, _) = await db.AuthorizeAsync(accessHash);
                credential = await db.CredentialAsync(snapshot.User.UserId, true) ?? throw InvalidCredentials();
                await db.CommitAsync(read);
            }
            var valid = passwords.Verify(snapshot.User.UserId, credential.Hash, request.CurrentPassword, ct);
            if (valid == PasswordVerificationResult.Failed) throw InvalidCredentials();
            var hash = passwords.Hash(snapshot.User.UserId, request.NewPassword, ct);
            await using var tx = await db.BeginAsync();
            var (user, family) = await db.AuthorizeAsync(accessHash);
            var current = await db.CredentialAsync(user.User.UserId, true);
            if (user.Version != snapshot.Version || current is null || current.Hash != credential.Hash) throw InvalidCredentials();
            await db.ExecuteAsync($"""
                UPDATE {schema}.users SET credential_version=credential_version+1 WHERE user_id=@p0;
                UPDATE {schema}.password_credentials SET password_hash=@p1,changed_at=@p2,failed_count=0,
                failure_window_started_at=NULL,locked_until=NULL WHERE user_id=@p0
                """, user.User.UserId, hash, db.Now);
            await using (var version = db.Command($"SELECT max(version) FROM {schema}.schema_migrations"))
                if (await version.ExecuteScalarAsync(ct) is int currentVersion && currentVersion >= 3)
                    await db.ExecuteAsync($"UPDATE {schema}.recovery_email_tokens SET consumed_at=@p0 WHERE user_id=@p1 AND consumed_at IS NULL", db.Now, user.User.UserId);
            await db.RevokeAsync(user.User.UserId, null, "password_change");
            await db.AuditAsync(user.User.UserId, family.Id, "password_change");
            await db.CommitAsync(tx);
            return true;
        }, ct);
    }

    private static AccountServiceException InvalidCredentials() => new(AccountFailure.InvalidCredentials);
}
