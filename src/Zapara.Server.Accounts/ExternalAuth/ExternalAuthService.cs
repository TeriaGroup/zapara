using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Accounts.ExternalResponses;
using Zapara.Server.Accounts.ExternalProviders;
using Zapara.Contracts.Accounts;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;

namespace Zapara.Server.Accounts;

public sealed partial class ExternalAuthService(AccountsDataSource dataSource, AccountsConfiguration configuration,
    TimeProvider clock, ExternalProviderRegistry providers, AccountPasswordWork? passwords = null)
{
    private readonly string schema = configuration.QuotedSchema;
    private readonly ExternalSecrets secrets = new(clock);
    private readonly AccountPasswordWork passwordWork = passwords ?? new();

    private async Task<T> DatabaseAsync<T>(Func<AccountRepository, Task<T>> operation, CancellationToken ct)
    {
        try
        {
            await using var connection = dataSource.CreateConnection();
            await connection.OpenAsync(ct);
            return await operation(new(connection, schema, clock, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is NpgsqlException or TimeoutException or OperationCanceledException)
        { throw new AccountServiceException(AccountFailure.DbUnavailable); }
    }

    public async Task<ExternalStartResponse> StartAsync(string provider, ExternalStartRequest request,
        string? accessToken = null, CancellationToken ct = default)
    {
        var adapter = providers.Get(provider);
        ValidateStart(request);
        var challenge = ExternalSecrets.Challenge(request.NativeChallenge);
        var id = Guid.NewGuid();
        var state = ExternalSecrets.Random();
        var verifier = ExternalSecrets.Random();
        var expires = clock.GetUtcNow().AddMinutes(10);
        var url = adapter.BuildAuthorizationUri(new(state), new(WebEncoders.Base64UrlEncode(ExternalSecrets.Hash(verifier))));
        secrets.Add(id, state, verifier, expires);
        try
        {
            return await DatabaseAsync(async db =>
            {
                await using var tx = await db.BeginAsync();
                AccountRow? user = null;
                FamilyRow? family = null;
                byte[]? proof = null;
                if (request.Purpose != "login")
                {
                    (user, family) = await db.AuthorizeAsync(AccountTokens.Hash(accessToken!, "za_"));
                    if (request.Purpose == "link")
                    {
                        proof = ProofHash(request.ProofToken);
                        await CheckProof(db, proof, user, family, "link:" + provider, null, false, ct);
                        await db.ExecuteAsync($"UPDATE {schema}.reauth_proofs SET reservation=@p0 WHERE proof_hash=@p1", id, proof);
                    }
                }
                await db.ExecuteAsync($"""
                    INSERT INTO {schema}.oauth_transactions(transaction_id,owner_id,purpose,provider,native_challenge,state_hash,
                      initiator_user_id,initiator_family_id,security_version,proof_hash,proof_purpose,status,expires_at,
                      return_kind,return_port,device_id,device_name,platform)
                    VALUES(@p0,@p1,@p2,@p3,@p4,@p5,CAST(@p6 AS uuid),CAST(@p7 AS uuid),CAST(@p8 AS bigint),CAST(@p9 AS bytea),@p10,
                      'pending',@p11,@p12,CAST(@p13 AS integer),@p14,@p15,@p16)
                    """, id, secrets.Owner, request.Purpose, provider, challenge, ExternalSecrets.Hash(state),
                    user?.User.UserId.ToString(), family?.Id.ToString(), user?.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    proof, request.ProofPurpose, expires, request.NativeReturn.Kind, request.NativeReturn.Port?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    request.Device.DeviceId, request.Device.DeviceName, request.Device.Platform);
                await db.CommitAsync(tx);
                return new ExternalStartResponse(id, url.AbsoluteUri, expires);
            }, ct);
        }
        catch { secrets.Remove(id); throw; }
    }

    private static void ValidateStart(ExternalStartRequest request)
    {
        if (request is null || request.Device is null || request.NativeReturn is null ||
            request.NativeChallengeMethod != "S256" || request.Purpose is not ("login" or "link" or "reauth")) throw ExternalAuthException.Invalid();
        if (request.NativeReturn.Kind != request.Device.Platform || request.NativeReturn.Kind switch
            { "windows" => request.NativeReturn.Port is < 1024 or > 65535 or null,
              "android" => request.NativeReturn.Port is not null, _ => true }) throw ExternalAuthException.Invalid();
        if (request.Purpose == "reauth") ValidateScope(request.ProofPurpose);
        else if (request.ProofPurpose is not null) throw ExternalAuthException.Invalid();
        if (request.Purpose != "link" && request.ProofToken is not null) throw ExternalAuthException.Invalid();
    }

    private static void ValidateScope(string? purpose)
    {
        if (purpose is not ("link:vk" or "link:yandex" or "unlink:vk" or "unlink:yandex" or "set_password"
            or "set_recovery_email" or "export" or "delete_account"))
            throw ExternalAuthException.Invalid();
    }
    private static byte[] ProofHash(string? proof)
    {
        ExternalSecrets.Token(proof, 43, 43);
        return ExternalSecrets.Hash(proof!);
    }

    private async Task CheckProof(AccountRepository db, byte[] hash, AccountRow user, FamilyRow family,
        string purpose, Guid? reservation, bool providerOnly, CancellationToken ct)
    {
        await using var command = db.Command($"""
            SELECT user_id,family_id,purpose,security_version,expires_at,consumed_at,reservation,provider_verified
            FROM {schema}.reauth_proofs WHERE proof_hash=@p0 FOR UPDATE
            """, hash);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.GetGuid(0) != user.User.UserId || reader.GetGuid(1) != family.Id ||
            reader.GetString(2) != purpose || reader.GetInt64(3) != user.Version || reader.GetFieldValue<DateTimeOffset>(4) <= db.Now ||
            !reader.IsDBNull(5) || (reader.IsDBNull(6) ? reservation is not null : reader.GetGuid(6) != reservation) ||
            (providerOnly && !reader.GetBoolean(7))) throw ExternalAuthException.Invalid();
    }
}
