using Zapara.Contracts.Accounts.ExternalResponses;

namespace Zapara.Server.Accounts;

public sealed partial class ExternalAuthService
{
    public Task<ExternalStatusResponse> StatusAsync(Guid transactionId, CancellationToken ct = default)
        => DatabaseAsync(async db =>
        {
            await using var command = db.Command($"SELECT status,expires_at,handoff_expires_at FROM {schema}.oauth_transactions WHERE transaction_id=@p0 AND owner_id=@p1", transactionId, secrets.Owner);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw ExternalAuthException.Gone();
            var status = reader.GetString(0);
            var expired = reader.GetFieldValue<DateTimeOffset>(1) <= db.Now
                || (status == "awaitingApp" && !reader.IsDBNull(2) && reader.GetFieldValue<DateTimeOffset>(2) <= db.Now);
            return new ExternalStatusResponse(expired ? "expired" : status);
        }, ct);
    public Task CleanupAsync(CancellationToken ct = default)
    {
        secrets.Cleanup();
        return DatabaseAsync(async db =>
        {
            // Only this Accounts module's expiring rows; includes attempts abandoned by a prior process.
            await db.ExecuteAsync($"""
                DELETE FROM {schema}.oauth_transactions WHERE transaction_id IN
                  (SELECT transaction_id FROM {schema}.oauth_transactions WHERE expires_at<=@p0 ORDER BY transaction_id LIMIT 1024 FOR UPDATE SKIP LOCKED)
                """, db.Now);
            await db.ExecuteAsync($"""
                DELETE FROM {schema}.reauth_proofs p WHERE p.proof_hash IN
                  (SELECT proof_hash FROM {schema}.reauth_proofs r WHERE expires_at<=@p0
                   AND NOT EXISTS(SELECT 1 FROM {schema}.oauth_transactions t WHERE t.proof_hash=r.proof_hash)
                   ORDER BY proof_hash LIMIT 1024 FOR UPDATE SKIP LOCKED)
                """, db.Now);
            return true;
        }, ct);
    }
    public Task<IReadOnlyList<ExternalIdentityResponse>> IdentitiesAsync(string accessToken, CancellationToken ct = default)
        => DatabaseAsync<IReadOnlyList<ExternalIdentityResponse>>(async db =>
        {
            await using var tx = await db.BeginAsync();
            var (user, _) = await db.AuthorizeAsync(AccountTokens.Hash(accessToken, "za_"));
            await using var command = db.Command($"SELECT provider,linked_at FROM {schema}.external_identities WHERE user_id=@p0 ORDER BY provider", user.User.UserId);
            var result = new List<ExternalIdentityResponse>();
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) result.Add(new(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)));
            await db.CommitAsync(tx);
            return result.AsReadOnly();
        }, ct);
}
