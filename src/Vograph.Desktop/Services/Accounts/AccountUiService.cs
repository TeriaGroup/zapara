using System.Security.Cryptography;
using System.Text;
using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Services.Profiles;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Accounts.ExternalResponses;

namespace Vograph.Desktop.Services.Accounts;

/// <summary>Installation-owned adapter; never activates or clears credentials itself.</summary>
public sealed class AccountUiService(AccountHttpClient client, IAccountSessionVault vault,
    ProfileSwitchCoordinator profiles)
{
    private readonly AccountSessionManager sessions = new(client, vault);

    public Task<AuthCapabilitiesResponse> CapabilitiesAsync(CancellationToken ct)
        => client.GetCapabilitiesAsync(ct);

    public Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken ct) => client.RegisterAsync(request, ct);

    public async Task<UserResponse?> CachedUserAsync(CancellationToken ct)
    {
        var expected = profiles.Snapshot.Identity;
        using var work = profiles.Current.Services.Work.Enter();
        work.ThrowIfStale();
        using var lease = await vault.AcquireAsync(ct);
        work.ThrowIfStale();
        var entry = lease.Read();
        return expected is not null && entry is not null && AccountSessionIdentity.From(entry.Session) == expected
            && profiles.Snapshot.Identity == expected ? entry.Session.User : null;
    }

    public Task<SupportThreadResponse[]> SupportAsync(CancellationToken ct)
        => RunAsync((s, token) => client.ListSupportAsync(s.AccessToken, token), ct);
    public Task<SupportThreadResponse> OpenSupportAsync(string subject, string body, CancellationToken ct)
        => RunAsync((s, token) => client.OpenSupportAsync(s.AccessToken, subject, body, token), ct);
    public Task<SupportThreadResponse> ContinueSupportAsync(Guid id, string body, CancellationToken ct)
        => RunAsync((s, token) => client.ContinueSupportAsync(s.AccessToken, id, body, token), ct);
    public Task<SupportThreadResponse> OpenSupportAsync(string subject, string body, IReadOnlyList<SupportUpload> files, CancellationToken ct)
        => RunAsync((s, token) => client.OpenSupportAsync(s.AccessToken, subject, body, files, token), ct);
    public Task<SupportThreadResponse> ContinueSupportAsync(Guid id, string body, IReadOnlyList<SupportUpload> files, CancellationToken ct)
        => RunAsync((s, token) => client.ContinueSupportAsync(s.AccessToken, id, body, files, token), ct);
    public Task<MeResponse> MeAsync(CancellationToken ct) => RunAsync((s, token) => client.GetMeAsync(s.AccessToken, token), ct);
    public Task<UserResponse> SaveAsync(string? name, CancellationToken ct)
        => RunAsync((s, token) => client.UpdateProfileAsync(s.AccessToken, new(name), token), ct);
    public Task<DevicesResponse> DevicesAsync(string? cursor, CancellationToken ct)
        => RunAsync((s, token) => client.ListDevicesAsync(s.AccessToken, 10, cursor, token), ct);

    public Task<ProfileSwitchResult?> RevokeAsync(Guid family, CancellationToken ct)
        => MutateAsync((s, token) => client.RevokeSessionAsync(s.AccessToken, family, token),
            profiles.Snapshot.Identity?.FamilyId == family, ct);
    public Task<ProfileSwitchResult?> RevokeAllAsync(CancellationToken ct)
        => MutateAsync((s, token) => client.RevokeAllAsync(s.AccessToken, token), true, ct);
    public Task<ProfileSwitchResult?> PasswordAsync(ChangePasswordRequest request, CancellationToken ct)
        => MutateAsync((s, token) => client.ChangePasswordAsync(s.AccessToken, request, token), true, ct);

    public Task<ExportJobResponse> CreateExportAsync(string password, CancellationToken ct)
    {
        var secret = AccountValidation.Password(password);
        return RunAsync(async (s, token) =>
        {
            var proof = await client.ReauthenticateAsync(s.AccessToken, new PasswordProofRequest(secret, "export"), token)
                .ConfigureAwait(false);
            return await client.CreateExportAsync(s.AccessToken, new ProofRequest(proof.ProofToken), token)
                .ConfigureAwait(false);
        }, ct);
    }

    public Task<ExportJobResponse> CreateExportWithProofAsync(string proofToken, CancellationToken ct)
        => RunAsync((s, token) => client.CreateExportAsync(s.AccessToken, new ProofRequest(proofToken), token), ct);

    public Task<ExportJobResponse> GetExportAsync(Guid exportId, CancellationToken ct)
        => RunAsync((s, token) => client.GetExportAsync(s.AccessToken, exportId, token), ct);

    public Task<AccountExportDownload> DownloadExportAsync(Guid exportId, CancellationToken ct)
        => RunAsync((s, token) => client.DownloadExportAsync(s.AccessToken, exportId, token), ct);

    public Task<ProfileSwitchResult?> DeleteAccountAsync(string password, CancellationToken ct)
    {
        var secret = AccountValidation.Password(password);
        return MutateAsync(async (s, token) =>
        {
            var proof = await client.ReauthenticateAsync(s.AccessToken, new PasswordProofRequest(secret, "delete_account"), token)
                .ConfigureAwait(false);
            await client.DeleteAccountAsync(s.AccessToken, new ProofRequest(proof.ProofToken), token).ConfigureAwait(false);
        }, true, ct);
    }

    public Task<ProfileSwitchResult?> DeleteAccountWithProofAsync(string proofToken, CancellationToken ct)
        => MutateAsync((s, token) => client.DeleteAccountAsync(s.AccessToken, new ProofRequest(proofToken), token), true, ct);

    public Task RequestPasswordResetAsync(string username, CancellationToken ct)
        => client.RequestPasswordResetAsync(new PasswordResetRequest(username), ct);

    public Task ConfirmPasswordResetAsync(string token, string password, CancellationToken ct)
        => client.ConfirmPasswordResetAsync(new PasswordResetConfirmRequest(token, password), ct);

    public Task<ProfileSwitchResult> CompleteExternalAsync(string provider, string? password,
        Func<string, Task> launch, CancellationToken ct)
        => CompleteExternalCoreAsync(provider, password, null, launch, ct);

    public Task<ProfileSwitchResult> CompleteExternalWithProofAsync(string provider, string proofToken,
        Func<string, Task> launch, CancellationToken ct)
        => CompleteExternalCoreAsync(provider, null, proofToken, launch, ct);

    private Task<ProfileSwitchResult> CompleteExternalCoreAsync(string provider, string? password, string? proofToken,
        Func<string, Task> launch, CancellationToken ct)
        => profiles.ExternalAsync(async token =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            using var callback = new ExternalLoopback();
            var verifier = Token();
            var expected = profiles.Snapshot.Identity;
            var link = password is not null || proofToken is not null;
            var start = !link
                ? await client.StartExternalAsync(provider, BuildStart("login", null, verifier, callback.Port), ct: timeout.Token)
                : await RunAsync(async (session, inner) =>
                {
                    var proof = proofToken ?? (await client.ReauthenticateAsync(session.AccessToken,
                        new PasswordProofRequest(AccountValidation.Password(password!), "link:" + provider), inner)).ProofToken;
                    return await client.StartExternalAsync(provider, BuildStart("link", proof, verifier, callback.Port), session.AccessToken, inner);
                }, timeout.Token);
            var remaining = start.ExpiresAt - DateTimeOffset.UtcNow;
            if (start.TransactionId == Guid.Empty || remaining <= TimeSpan.Zero
                || !Uri.TryCreate(start.AuthorizeUrl, UriKind.Absolute, out var authorize) || authorize.Scheme != "https"
                || !string.IsNullOrEmpty(authorize.UserInfo))
                throw new AccountClientException(AccountClientFailure.InvalidPayload);
            timeout.CancelAfter(remaining < TimeSpan.FromMinutes(10) ? remaining : TimeSpan.FromMinutes(10));
            await launch(start.AuthorizeUrl);
            var code = await callback.ReceiveAsync(start.TransactionId, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            if (profiles.Snapshot.Identity != expected) throw new AccountClientException(AccountClientFailure.SessionChanged);
            var request = new ExternalExchangeRequest(start.TransactionId, verifier, code);
            var result = !link ? await client.ExchangeExternalAsync(request, ct: timeout.Token)
                : await RunAsync((session, inner) => client.ExchangeExternalAsync(request, session.AccessToken, inner), timeout.Token);
            if (result.Status != "completed" || (!link ? result.Session is null : result.Session is not null))
                throw new AccountClientException(AccountClientFailure.InvalidPayload);
            return result.Session;
        }, password is null && proofToken is null, ct);

    public Task<ReauthResponse> ExternalProofAsync(string provider, string purpose, Func<string, Task> launch, CancellationToken ct)
        => RunAsync(async (session, token) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            using var callback = new ExternalLoopback();
            var verifier = Token();
            var start = await client.StartExternalAsync(provider,
                BuildStart("reauth", null, verifier, callback.Port, purpose), session.AccessToken, timeout.Token);
            var remaining = start.ExpiresAt - DateTimeOffset.UtcNow;
            if (start.TransactionId == Guid.Empty || remaining <= TimeSpan.Zero ||
                !Uri.TryCreate(start.AuthorizeUrl, UriKind.Absolute, out var authorize) || authorize.Scheme != "https" ||
                !string.IsNullOrEmpty(authorize.UserInfo)) throw new AccountClientException(AccountClientFailure.InvalidPayload);
            timeout.CancelAfter(remaining < TimeSpan.FromMinutes(10) ? remaining : TimeSpan.FromMinutes(10));
            await launch(start.AuthorizeUrl);
            var code = await callback.ReceiveAsync(start.TransactionId, timeout.Token);
            var result = await client.ExchangeExternalAsync(new(start.TransactionId, verifier, code), session.AccessToken, timeout.Token);
            if (result.Status != "completed" || result.Session is not null || result.Proof?.Purpose != purpose)
                throw new AccountClientException(AccountClientFailure.InvalidPayload);
            return result.Proof;
        }, ct);

    public Task<ExternalStatusResponse> GetExternalStatusAsync(Guid transactionId, CancellationToken ct)
        => client.GetExternalStatusAsync(transactionId, ct);

    public Task<ExternalExchangeResponse> ExchangeExternalAsync(ExternalExchangeRequest request, string? accessToken,
        CancellationToken ct)
        => client.ExchangeExternalAsync(request, accessToken, ct);

    public Task<IReadOnlyList<ExternalIdentityResponse>> ListIdentitiesAsync(CancellationToken ct)
        => RunAsync((s, token) => client.ListIdentitiesAsync(s.AccessToken, token), ct);

    public Task UnlinkIdentityAsync(string provider, string password, CancellationToken ct)
    {
        var secret = AccountValidation.Password(password);
        return RunAsync(async (s, token) =>
        {
            var proof = await client.ReauthenticateAsync(s.AccessToken,
                new PasswordProofRequest(secret, "unlink:" + provider), token).ConfigureAwait(false);
            await client.UnlinkIdentityAsync(s.AccessToken, provider, new ProofRequest(proof.ProofToken), token)
                .ConfigureAwait(false);
            return true;
        }, ct);
    }

    public Task UnlinkIdentityWithProofAsync(string provider, string proofToken, CancellationToken ct)
        => RunAsync(async (s, token) =>
        {
            await client.UnlinkIdentityAsync(s.AccessToken, provider, new ProofRequest(proofToken), token);
            return true;
        }, ct);

    private ExternalStartRequest BuildStart(string purpose, string? proofToken, string verifier, int port, string? proofPurpose = null)
    {
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var deviceId = ProfileInstallation.LoadOrCreate(profiles.Current.Services.Shared.GlobalDataDir);
        return new(purpose, challenge, "S256", new DeviceInput(deviceId, "Windows", "windows"),
            new NativeReturn("windows", port), proofToken, proofPurpose);
    }

    private static string Token()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private async Task<T> RunAsync<T>(Func<SessionResponse, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        var current = profiles.Current;
        var expected = profiles.Snapshot.Identity ?? throw new AccountClientException(AccountClientFailure.ReauthenticationRequired);
        using var work = current.Services.Work.Enter();
        work.ThrowIfStale();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, work.Token);
        var session = await sessions.GetValidSessionAsync(linked.Token);
        Check();
        var result = await action(session, linked.Token);
        Check();
        return result;

        void Check()
        {
            work.ThrowIfStale();
            if (profiles.Snapshot.Identity != expected || AccountSessionIdentity.From(session) != expected || profiles.Current != current)
                throw new AccountClientException(AccountClientFailure.SessionChanged);
        }
    }

    private async Task<ProfileSwitchResult?> MutateAsync(Func<SessionResponse, CancellationToken, Task> action,
        bool leave, CancellationToken ct)
    {
        var expected = profiles.Snapshot.Identity;
        await RunAsync(async (session, token) => { await action(session, token); return true; }, ct);
        // RunAsync has released its outer work lease. A transition here must never drain itself.
        if (profiles.Snapshot.Identity != expected) throw new AccountClientException(AccountClientFailure.SessionChanged);
        return leave ? await profiles.LogoutAsync(ct) : null;
    }
}
