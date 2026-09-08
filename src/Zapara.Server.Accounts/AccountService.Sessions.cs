using Zapara.Contracts.Accounts;

namespace Zapara.Server.Accounts;

public sealed partial class AccountService
{
    public Task<SessionResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = AccountTokens.Hash(refreshToken, "zr_");
        return DatabaseAsync(async db =>
        {
            var identity = await db.ResolveAsync(hash, true) ?? throw AccountRepository.InvalidSession();
            await using var tx = await db.BeginAsync();
            var user = await db.UserAsync(identity.UserId, locked: true);
            var family = await db.FamilyAsync(identity.FamilyId);
            var token = await db.TokenAsync(hash, true);
            db.CheckLive(user, family, token);
            if (token!.Consumed)
            {
                await db.RevokeAsync(identity.UserId, identity.FamilyId, "refresh_replay");
                await db.AuditAsync(identity.UserId, identity.FamilyId, "refresh_replay");
                await db.CommitAsync(tx);
                throw AccountRepository.InvalidSession();
            }
            await db.ExecuteAsync($"UPDATE {schema}.refresh_tokens SET consumed_at=@p0 WHERE token_hash=@p1", db.Now, hash);
            var session = await db.IssueAsync(user!.User, family!.Id, family.Expires);
            await db.ExecuteAsync($"""
                UPDATE {schema}.refresh_tokens SET replacement_hash=@p0 WHERE token_hash=@p1;
                UPDATE {schema}.session_families SET last_seen_at=@p2 WHERE family_id=@p3
                """, AccountTokens.Hash(session.RefreshToken, "zr_"), hash, db.Now, family.Id);
            await db.CommitAsync(tx);
            return session;
        }, ct);
    }

    public Task<AccountAuthentication> AuthenticateAsync(string accessToken, CancellationToken ct = default)
        => AuthorizedAsync(accessToken, (db, user, family) => Task.FromResult(
            new AccountAuthentication(user.User, family.Id, family.AuthenticatedAt, user.Version)), ct);

    public Task<MeResponse> GetMeAsync(string accessToken, CancellationToken ct = default)
        => AuthorizedAsync(accessToken, async (db, user, family) =>
            new MeResponse(user.User, family.Id, await db.AuthenticationMethodsAsync(user.User.UserId)), ct);

    public Task<UserResponse> UpdateProfileAsync(string accessToken, UpdateProfileRequest request, CancellationToken ct = default)
    {
        Required(request);
        Validate(() => AccountValidation.DisplayName(request.DisplayName));
        return AuthorizedAsync(accessToken, async (db, user, family) =>
        {
            await db.ExecuteAsync($"UPDATE {schema}.users SET display_name=@p0 WHERE user_id=@p1", request.DisplayName, user.User.UserId);
            return new UserResponse(user.User.UserId, user.User.Username, request.DisplayName, user.User.CreatedAt);
        }, ct);
    }

    public Task LogoutAsync(string accessToken, CancellationToken ct = default)
        => RevokeOperationAsync(accessToken, null, "logout", ct);

    public Task RevokeSessionAsync(string accessToken, Guid targetFamilyId, CancellationToken ct = default)
        => RevokeOperationAsync(accessToken, targetFamilyId, "revoke", ct);

    public Task RevokeAllAsync(string accessToken, CancellationToken ct = default)
        => RevokeOperationAsync(accessToken, null, "revoke_all", ct);

    private Task RevokeOperationAsync(string accessToken, Guid? target, string reason, CancellationToken ct)
        => AuthorizedAsync(accessToken, async (db, user, family) =>
        {
            if (target.HasValue)
            {
                // Ownership check precedes target locking: never lock another actor's family.
                await using var command = db.Command($"SELECT 1 FROM {schema}.session_families WHERE family_id=@p0 AND user_id=@p1", target.Value, user.User.UserId);
                if (await command.ExecuteScalarAsync(ct) is null) throw new AccountServiceException(AccountFailure.SessionNotFound);
                await db.FamilyAsync(target.Value);
            }
            await db.RevokeAsync(user.User.UserId, reason == "revoke_all" ? null : target ?? family.Id, reason);
            await db.AuditAsync(user.User.UserId, target ?? family.Id, reason);
            return true;
        }, ct);

    private Task<T> AuthorizedAsync<T>(string accessToken,
        Func<AccountRepository, AccountRow, FamilyRow, Task<T>> operation, CancellationToken ct)
        => ExecuteMutationAsync(accessToken, (context, _) => operation(context.Repository, context.User, context.Family), ct);
}
