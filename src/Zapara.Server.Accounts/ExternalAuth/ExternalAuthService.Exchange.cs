using System.Buffers.Binary;
using System.Security.Cryptography;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Accounts.ExternalResponses;

namespace Zapara.Server.Accounts;

public sealed partial class ExternalAuthService
{
    public Task<ExternalExchangeResponse> ExchangeAsync(ExternalExchangeRequest request, string? accessToken = null,
        CancellationToken ct = default)
    {
        if (request is null || request.TransactionId == Guid.Empty) throw ExternalAuthException.Invalid();
        ExternalSecrets.Token(request.NativeVerifier);
        ExternalSecrets.Token(request.HandoffCode, 43, 43);
        return DatabaseAsync(async db =>
        {
            var snapshot = await ReadTransaction(db, request.TransactionId, false, ct);
            CheckNative(snapshot, request, db.Now);
            await using var tx = await db.BeginAsync();
            await IdentityLock(db, snapshot.Provider, snapshot.Subject!);
            var row = await ReadTransaction(db, request.TransactionId, true, ct);
            CheckNative(row, request, db.Now);
            if (row.Provider != snapshot.Provider || row.Subject != snapshot.Subject) throw ExternalAuthException.Invalid();
            var result = row.Purpose == "login" ? await LoginExternal(db, row, ct)
                : await CompleteBound(db, row, accessToken, ct);
            // User/family locks can wait beyond the handoff deadline. Roll back all issuance in that case.
            CheckNative(row, request, db.Now);
            await db.ExecuteAsync($"""
                UPDATE {schema}.oauth_transactions SET status='completed',consumed_at=@p0,subject=NULL,display_name=NULL,
                handoff_hash=NULL,handoff_expires_at=NULL WHERE transaction_id=@p1
                """, db.Now, row.Id);
            await db.CommitAsync(tx);
            return result;
        }, ct);
    }

    private static void CheckNative(ExternalTransaction row, ExternalExchangeRequest request, DateTimeOffset now)
    {
        if (row.Status != "awaitingApp" || row.Expires <= now || row.HandoffExpires is null || row.HandoffExpires <= now)
            throw ExternalAuthException.Gone();
        if (row.Subject is null || row.HandoffHash is null ||
            !CryptographicOperations.FixedTimeEquals(row.NativeChallenge, ExternalSecrets.Hash(request.NativeVerifier)) ||
            !CryptographicOperations.FixedTimeEquals(row.HandoffHash, ExternalSecrets.Hash(request.HandoffCode))) throw ExternalAuthException.Invalid();
    }

    private Task IdentityLock(AccountRepository db, string provider, string subject)
        => db.ExecuteAsync("SELECT pg_advisory_xact_lock(@p0)", BinaryPrimitives.ReadInt64BigEndian(
            ExternalSecrets.Hash(schema + "\nidentity\n" + provider + "\n" + subject)));

    private async Task<Guid?> IdentityOwner(AccountRepository db, string provider, string subject, CancellationToken ct)
    {
        await using var command = db.Command($"SELECT user_id FROM {schema}.external_identities WHERE provider=@p0 AND subject=@p1", provider, subject);
        return await command.ExecuteScalarAsync(ct) is Guid id ? id : null;
    }

    private async Task<ExternalExchangeResponse> LoginExternal(AccountRepository db, ExternalTransaction row, CancellationToken ct)
    {
        if (row.UserId is not null || row.FamilyId is not null || row.Version is not null || row.ProofHash is not null) throw ExternalAuthException.Invalid();
        var owner = await IdentityOwner(db, row.Provider, row.Subject!, ct);
        AccountRow user;
        if (owner is not null) user = await db.UserAsync(owner, locked: true) ?? throw ExternalAuthException.Invalid();
        else
        {
            var id = Guid.NewGuid();
            // 120 random bits, ASCII; uniqueness remains database-enforced. Never use provider email.
            var username = "u_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(15)).ToLowerInvariant();
            await db.ExecuteAsync($"""
                INSERT INTO {schema}.users(user_id,username,normalized_username,display_name,created_at,status)
                VALUES(@p0,@p1,@p1,@p2,@p3,'active')
                """, id, username, row.DisplayName, db.Now);
            await db.ExecuteAsync($"INSERT INTO {schema}.external_identities VALUES(@p0,@p1,@p2,@p3)", id, row.Provider, row.Subject, db.Now);
            user = await db.UserAsync(id, locked: true) ?? throw ExternalAuthException.Invalid();
        }
        if (user.Status != "active") throw ExternalAuthException.Invalid();
        var family = Guid.NewGuid();
        var expiry = db.Now.AddDays(30);
        await db.ExecuteAsync($"""
            INSERT INTO {schema}.session_families(family_id,user_id,device_id,device_name,platform,created_at,authenticated_at,last_seen_at,expires_at)
            VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p5,@p5,@p6)
            """, family, user.User.UserId, row.Device.DeviceId, row.Device.DeviceName, row.Device.Platform, db.Now, expiry);
        var session = await db.IssueAsync(user.User, family, expiry);
        await db.AuditAsync(user.User.UserId, family, "login");
        await db.ExecuteAsync($"UPDATE {schema}.oauth_transactions SET resolved_user_id=@p0 WHERE transaction_id=@p1", user.User.UserId, row.Id);
        return new("completed", session);
    }
}
