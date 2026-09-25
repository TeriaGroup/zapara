using Zapara.Contracts.Accounts;

namespace Zapara.Server.Accounts;

internal sealed partial class AccountRepository
{
    internal async Task<(Guid UserId, Guid FamilyId)?> ResolveAsync(byte[] hash, bool refresh)
    {
        await using var command = Command($"""
            SELECT f.user_id,f.family_id FROM {schema}.{(refresh ? "refresh_tokens" : "access_tokens")} t
            JOIN {schema}.session_families f ON f.family_id=t.family_id WHERE t.token_hash=@p0
            """, hash);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetGuid(0), reader.GetGuid(1)) : null;
    }

    internal async Task<FamilyRow?> FamilyAsync(Guid id)
    {
        await using var command = Command($"""
            SELECT family_id,user_id,authenticated_at,expires_at,revoked_at
            FROM {schema}.session_families WHERE family_id=@p0 FOR UPDATE
            """, id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3), !reader.IsDBNull(4)) : null;
    }

    internal async Task<TokenRow?> TokenAsync(byte[] hash, bool refresh)
    {
        await using var command = Command($"""
            SELECT family_id,expires_at,{(refresh ? "consumed_at IS NOT NULL" : "false")},
                {(refresh ? "replacement_hash" : "NULL::bytea")}
            FROM {schema}.{(refresh ? "refresh_tokens" : "access_tokens")} WHERE token_hash=@p0 FOR UPDATE
            """, hash);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(1),
            reader.GetBoolean(2), reader.IsDBNull(3) ? null : reader.GetFieldValue<byte[]>(3)) : null;
    }

    internal async Task<(AccountRow User, FamilyRow Family)> AuthorizeAsync(byte[] accessHash)
    {
        var identity = await ResolveAsync(accessHash, false) ?? throw InvalidSession();
        var user = await UserAsync(identity.UserId, locked: true);
        var family = await FamilyAsync(identity.FamilyId);
        var token = await TokenAsync(accessHash, false);
        CheckLive(user, family, token);
        return (user!, family!);
    }

    internal void CheckLive(AccountRow? user, FamilyRow? family, TokenRow? token)
    {
        var now = Now;
        if (user is null || user.Status != "active" || family is null || family.UserId != user.User.UserId ||
            family.Revoked || now >= family.Expires || token is null || token.FamilyId != family.Id || now >= token.Expires)
            throw InvalidSession();
    }

    internal async Task<SessionResponse> IssueAsync(UserResponse user, Guid familyId, DateTimeOffset expiry,
        string? accessToken = null, string? refreshToken = null)
    {
        var now = Now;
        // Npgsql persists timestamps at microsecond precision. The initial response must match
        // the family expiry subsequently read from PostgreSQL during the first refresh.
        expiry = new DateTimeOffset(expiry.Ticks - expiry.Ticks % 10, TimeSpan.Zero);
        if (now >= expiry) throw InvalidSession();
        var access = accessToken ?? AccountTokens.Create("za_");
        var refresh = refreshToken ?? AccountTokens.Create("zr_");
        var accessExpiry = now.AddMinutes(15) < expiry ? now.AddMinutes(15) : expiry;
        // PostgreSQL timestamps retain microseconds; return the same value that a retry reads back.
        accessExpiry = new DateTimeOffset(accessExpiry.Ticks - accessExpiry.Ticks % 10, TimeSpan.Zero);
        await ExecuteAsync($"DELETE FROM {schema}.access_tokens WHERE family_id=@p0", familyId);
        await ExecuteAsync($"""
            INSERT INTO {schema}.access_tokens(token_hash,family_id,created_at,expires_at) VALUES(@p0,@p1,@p2,@p3);
            INSERT INTO {schema}.refresh_tokens(token_hash,family_id,created_at,expires_at) VALUES(@p4,@p1,@p2,@p5)
            """, AccountTokens.Hash(access, "za_"), familyId, now, accessExpiry, AccountTokens.Hash(refresh, "zr_"), expiry);
        return new(user, familyId, access, refresh, "Bearer", accessExpiry, expiry);
    }

    internal async Task<SessionResponse?> RecoverRetryAsync(UserResponse user, FamilyRow family,
        string accessToken, string refreshToken)
    {
        await using var command = Command($"""
            SELECT a.expires_at FROM {schema}.access_tokens a
            JOIN {schema}.refresh_tokens r ON r.family_id=a.family_id
            WHERE a.family_id=@p0 AND a.token_hash=@p1 AND r.token_hash=@p2 AND r.consumed_at IS NULL
            """, family.Id, AccountTokens.Hash(accessToken, "za_"), AccountTokens.Hash(refreshToken, "zr_"));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new SessionResponse(user, family.Id, accessToken, refreshToken, "Bearer",
                reader.GetFieldValue<DateTimeOffset>(0), family.Expires)
            : null;
    }

    internal Task RevokeAsync(Guid userId, Guid? familyId, string reason)
        => ExecuteAsync($"""
            UPDATE {schema}.session_families SET revoked_at=@p0,revocation_reason=@p1
            WHERE user_id=@p2 AND revoked_at IS NULL {(familyId.HasValue ? "AND family_id=@p3" : "")}
            """, familyId.HasValue ? [Now, reason, userId, familyId.Value] : [Now, reason, userId]);

    internal static AccountServiceException InvalidSession() => new(AccountFailure.InvalidSession);
}

internal sealed record FamilyRow(Guid Id, Guid UserId, DateTimeOffset AuthenticatedAt, DateTimeOffset Expires, bool Revoked);
internal sealed record TokenRow(Guid FamilyId, DateTimeOffset Expires, bool Consumed, byte[]? ReplacementHash);
