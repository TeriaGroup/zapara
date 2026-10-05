using Zapara.Contracts.Accounts.ExternalResponses;

namespace Zapara.Server.Accounts;

public sealed partial class ExternalAuthService
{
    private async Task<ExternalExchangeResponse> CompleteBound(AccountRepository db, ExternalTransaction row,
        string? accessToken, CancellationToken ct)
    {
        if (row.Purpose is not ("link" or "reauth")) throw ExternalAuthException.Invalid();
        var owner = await IdentityOwner(db, row.Provider, row.Subject!, ct);
        var (user, family) = await db.AuthorizeAsync(AccountTokens.Hash(accessToken!, "za_"));
        if (row.UserId != user.User.UserId || row.FamilyId != family.Id || row.Version != user.Version) throw ExternalAuthException.Invalid();
        if (row.Purpose == "reauth")
        {
            if (owner != user.User.UserId || row.ProofHash is not null) throw ExternalAuthException.Invalid();
            return new("completed", Proof: await IssueProof(db, user, family, row.ProofPurpose!, true));
        }
        if (row.ProofHash is null) throw ExternalAuthException.Invalid();
        await CheckProof(db, row.ProofHash, user, family, "link:" + row.Provider, row.Id, false, ct);
        if (owner is not null && owner != user.User.UserId) throw new ExternalAuthException(409, "identity_unavailable");
        await using var check = db.Command($"SELECT subject FROM {schema}.external_identities WHERE user_id=@p0 AND provider=@p1", user.User.UserId, row.Provider);
        if (await check.ExecuteScalarAsync(ct) is string subject && subject != row.Subject) throw new ExternalAuthException(409, "identity_unavailable");
        if (owner is null)
            await db.ExecuteAsync($"INSERT INTO {schema}.external_identities VALUES(@p0,@p1,@p2,@p3)", user.User.UserId, row.Provider, row.Subject, db.Now);
        await db.ExecuteAsync($"UPDATE {schema}.reauth_proofs SET consumed_at=@p0 WHERE proof_hash=@p1", db.Now, row.ProofHash);
        await db.ExecuteAsync($"UPDATE {schema}.users SET credential_version=credential_version+1 WHERE user_id=@p0", user.User.UserId);
        return new("completed");
    }
}
