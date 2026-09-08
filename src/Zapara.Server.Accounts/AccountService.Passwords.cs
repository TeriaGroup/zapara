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

    public Task<SessionResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        Required(request);
        var normalized = Validate(() => AccountValidation.NormalizeUsername(request.Username));
        Validate(() => AccountValidation.Password(request.Password));
        Validate(() => AccountValidation.Id(request.Device.DeviceId));
        Validate(() => AccountValidation.DeviceName(request.Device.DeviceName));
        Validate(() => AccountValidation.Platform(request.Device.Platform));
        return DatabaseAsync(async db =>
        {
            var snapshot = await db.UserAsync(username: normalized);
            var credential = snapshot is null ? null : await db.CredentialAsync(snapshot.User.UserId);
            if (snapshot is null || snapshot.Status != "active" || credential is null || credential.LockedUntil > db.Now)
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
                currentCredential is null || currentCredential.Hash != credential.Hash || currentCredential.LockedUntil > db.Now)
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

    private async Task FailedLoginAsync(AccountRepository db, Guid userId, CredentialRow credential)
    {
        var now = db.Now;
        var reset = credential.Window is null || now >= credential.Window.Value.AddMinutes(15);
        var count = reset ? 1 : Math.Min(5, credential.Failures + 1);
        var window = reset ? now : credential.Window!.Value;
        await db.ExecuteAsync($"""
            UPDATE {schema}.password_credentials SET failed_count=@p0,failure_window_started_at=@p1,
            locked_until=CASE WHEN @p0>=5 THEN @p2 ELSE NULL END WHERE user_id=@p3
            """, count, window, now.AddMinutes(15), userId);
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
            await db.RevokeAsync(user.User.UserId, null, "password_change");
            await db.AuditAsync(user.User.UserId, family.Id, "password_change");
            await db.CommitAsync(tx);
            return true;
        }, ct);
    }

    private static AccountServiceException InvalidCredentials() => new(AccountFailure.InvalidCredentials);
}
