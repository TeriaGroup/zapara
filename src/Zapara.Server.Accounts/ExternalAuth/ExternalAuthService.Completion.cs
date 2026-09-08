using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Accounts.ExternalResponses;
using Microsoft.AspNetCore.WebUtilities;
using Zapara.Server.Accounts.ExternalProviders;

namespace Zapara.Server.Accounts;

public sealed partial class ExternalAuthService
{
    public async Task<Uri> CallbackAsync(string provider, Uri callbackUri, string state, string? code,
        string? deviceId = null, bool providerError = false, CancellationToken ct = default)
    {
        providers.VerifyUri(provider, callbackUri);
        ExternalSecrets.Token(state, 43, 43);
        var id = await DatabaseAsync(async db =>
        {
            // A single autocommit claim, no user/identity locks and no transaction held over HTTP.
            await using var command = db.Command($"""
                UPDATE {schema}.oauth_transactions SET status='callbackClaimed'
                WHERE state_hash=@p0 AND provider=@p1 AND owner_id=@p2 AND status='pending' AND expires_at>@p3
                RETURNING transaction_id
                """, ExternalSecrets.Hash(state), provider, secrets.Owner, db.Now);
            return await command.ExecuteScalarAsync(ct) is Guid value ? value : throw ExternalAuthException.Gone();
        }, ct);
        try
        {
            var secret = secrets.Take(id);
            if (secret.State != state || providerError || code is null || code.Length is < 1 or > 8192 ||
                deviceId?.Length > 8192) throw ExternalAuthException.Invalid();
            var identity = await providers.Get(provider).ExchangeIdentityAsync(code, new(secret.State), new(secret.Verifier), deviceId, ct);
            var handoff = ExternalSecrets.Random();
            return await DatabaseAsync(async db =>
            {
                await using var tx = await db.BeginAsync();
                var row = await ReadTransaction(db, id, true, ct);
                if (row.Status != "callbackClaimed" || row.Expires <= db.Now || identity.Provider != row.Provider) throw ExternalAuthException.Gone();
                var expires = db.Now.AddSeconds(60) < row.Expires ? db.Now.AddSeconds(60) : row.Expires;
                await db.ExecuteAsync($"""
                    UPDATE {schema}.oauth_transactions SET status='awaitingApp',subject=@p0,display_name=@p1,
                    handoff_hash=@p2,handoff_expires_at=@p3 WHERE transaction_id=@p4
                    """, identity.Subject, identity.DisplayName, ExternalSecrets.Hash(handoff), expires, id);
                await db.CommitAsync(tx);
                var destination = row.ReturnKind == "windows" ? $"http://127.0.0.1:{row.ReturnPort}/zapara/oauth/callback" : "zapara://auth/external";
                return new Uri(QueryHelpers.AddQueryString(destination, new Dictionary<string, string?>
                    { ["transactionId"] = id.ToString("D"), ["handoffCode"] = handoff }));
            }, ct);
        }
        catch (Exception e) when (e is ExternalProviderException or ExternalAuthException or OperationCanceledException)
        {
            await FailAttempt(id);
            if (e is OperationCanceledException && ct.IsCancellationRequested) throw;
            throw ExternalAuthException.Invalid();
        }
    }

    private async Task FailAttempt(Guid id)
    {
        secrets.Remove(id);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await DatabaseAsync(async db =>
        {
            await db.ExecuteAsync($"""
                UPDATE {schema}.oauth_transactions SET status='failed',subject=NULL,display_name=NULL,handoff_hash=NULL,handoff_expires_at=NULL
                WHERE transaction_id=@p0 AND owner_id=@p1 AND status='callbackClaimed'
                """, id, secrets.Owner);
            return true;
        }, timeout.Token);
    }
}
