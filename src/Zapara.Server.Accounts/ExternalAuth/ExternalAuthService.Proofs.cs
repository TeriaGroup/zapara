using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Accounts.ExternalResponses;
using Zapara.Contracts.Accounts;
using Microsoft.AspNetCore.Identity;

namespace Zapara.Server.Accounts;

public sealed partial class ExternalAuthService
{
    public Task<ReauthResponse> PasswordProofAsync(string accessToken, PasswordProofRequest request,
        CancellationToken ct = default)
    {
        if (request is null) throw ExternalAuthException.Invalid();
        ValidateScope(request.Purpose);
        AccountValidation.Password(request.CurrentPassword);
        var access = AccountTokens.Hash(accessToken, "za_");
        return DatabaseAsync(async db =>
        {
            AccountRow snapshot;
            CredentialRow credential;
            await using (var read = await db.BeginAsync())
            {
                (snapshot, _) = await db.AuthorizeAsync(access);
                credential = await db.CredentialAsync(snapshot.User.UserId) ?? throw ExternalAuthException.Invalid();
                await db.CommitAsync(read);
            }
            if (passwordWork.Verify(snapshot.User.UserId, credential.Hash, request.CurrentPassword, ct) == PasswordVerificationResult.Failed)
                throw ExternalAuthException.Invalid();
            await using var tx = await db.BeginAsync();
            var (user, family) = await db.AuthorizeAsync(access);
            var current = await db.CredentialAsync(user.User.UserId);
            if (user.Version != snapshot.Version || current?.Hash != credential.Hash) throw ExternalAuthException.Invalid();
            var result = await IssueProof(db, user, family, request.Purpose, false);
            await db.CommitAsync(tx);
            return result;
        }, ct);
    }

    private async Task<ReauthResponse> IssueProof(AccountRepository db, AccountRow user, FamilyRow family, string purpose, bool verified)
    {
        ValidateScope(purpose);
        var token = ExternalSecrets.Random();
        var expires = db.Now.AddMinutes(5);
        await db.ExecuteAsync($"""
            INSERT INTO {schema}.reauth_proofs(proof_hash,user_id,family_id,purpose,security_version,provider_verified,expires_at)
            VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)
            """, ExternalSecrets.Hash(token), user.User.UserId, family.Id, purpose, user.Version, verified, expires);
        return new(token, purpose, expires);
    }
    public Task SetFirstPasswordAsync(string accessToken, FirstPasswordRequest request,
        CancellationToken ct = default)
    {
        if (request is null) throw ExternalAuthException.Invalid();
        AccountValidation.Password(request.NewPassword);
        var proof = ProofHash(request.ProofToken);
        var access = AccountTokens.Hash(accessToken, "za_");
        return DatabaseAsync(async db =>
        {
            var identity = await db.ResolveAsync(access, false) ?? throw AccountRepository.InvalidSession();
            var password = passwordWork.Hash(identity.UserId, request.NewPassword, ct);
            await using var tx = await db.BeginAsync();
            // User first serializes all family mutations. Lock all families before the proof.
            await db.UserAsync(identity.UserId, locked: true);
            await db.ExecuteAsync($"SELECT family_id FROM {schema}.session_families WHERE user_id=@p0 ORDER BY family_id FOR UPDATE", identity.UserId);
            var (user, family) = await db.AuthorizeAsync(access);
            if (await db.CredentialAsync(user.User.UserId) is not null) throw new ExternalAuthException(409, "password_already_set");
            await CheckProof(db, proof, user, family, "set_password", null, true, ct);
            await db.ExecuteAsync($"INSERT INTO {schema}.password_credentials(user_id,password_hash,changed_at) VALUES(@p0,@p1,@p2)", user.User.UserId, password, db.Now);
            await db.ExecuteAsync($"UPDATE {schema}.users SET credential_version=credential_version+1 WHERE user_id=@p0", user.User.UserId);
            await db.ExecuteAsync($"UPDATE {schema}.reauth_proofs SET consumed_at=@p0 WHERE proof_hash=@p1", db.Now, proof);
            await db.RevokeAsync(user.User.UserId, null, "password_change");
            await db.AuditAsync(user.User.UserId, family.Id, "password_change");
            await db.CommitAsync(tx);
            return true;
        }, ct);
    }
}
