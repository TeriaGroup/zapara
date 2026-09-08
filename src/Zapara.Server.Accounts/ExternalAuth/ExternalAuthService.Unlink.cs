namespace Zapara.Server.Accounts;

public sealed partial class ExternalAuthService
{
    public Task UnlinkAsync(string accessToken, string provider, string proofToken, CancellationToken ct = default)
    {
        if (provider is not ("vk" or "yandex")) throw ExternalAuthException.Invalid();
        var proof = ProofHash(proofToken);
        var access = AccountTokens.Hash(accessToken, "za_");
        return DatabaseAsync(async db =>
        {
            var snapshot = await db.ResolveAsync(access, false) ?? throw AccountRepository.InvalidSession();
            await using var find = db.Command($"SELECT subject FROM {schema}.external_identities WHERE user_id=@p0 AND provider=@p1", snapshot.UserId, provider);
            var subject = await find.ExecuteScalarAsync(ct) as string ?? throw new ExternalAuthException(409, "identity_unavailable");
            await using var tx = await db.BeginAsync();
            await IdentityLock(db, provider, subject);
            var (user, family) = await db.AuthorizeAsync(access);
            if (await IdentityOwner(db, provider, subject, ct) != user.User.UserId) throw ExternalAuthException.Invalid();
            await CheckProof(db, proof, user, family, "unlink:" + provider, null, false, ct);
            if ((await db.AuthenticationMethodsAsync(user.User.UserId)).Count <= 1) throw new ExternalAuthException(409, "last_login_method");
            await db.ExecuteAsync($"DELETE FROM {schema}.external_identities WHERE provider=@p0 AND subject=@p1 AND user_id=@p2", provider, subject, user.User.UserId);
            await db.ExecuteAsync($"UPDATE {schema}.reauth_proofs SET consumed_at=@p0 WHERE proof_hash=@p1", db.Now, proof);
            await db.ExecuteAsync($"UPDATE {schema}.users SET credential_version=credential_version+1 WHERE user_id=@p0", user.User.UserId);
            await db.CommitAsync(tx);
            return true;
        }, ct);
    }
}
