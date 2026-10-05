using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Accounts.ExternalResponses;

namespace Vograph.Core.Services.Accounts;

public sealed record AccountExportDownload(byte[] Payload, string FileName);

internal sealed class AccountEmptyObject;

public sealed partial class AccountHttpClient
{
    public Task<ExportJobResponse> CreateExportAsync(string accessToken, ProofRequest request, CancellationToken ct = default)
        => SendAsync<ExportJobResponse>(HttpMethod.Post, "account/exports", Required(request), Access(accessToken), 202, ct);

    public Task<ExportJobResponse> GetExportAsync(string accessToken, Guid exportId, CancellationToken ct = default)
        => SendAsync<ExportJobResponse>(HttpMethod.Get, $"account/exports/{AccountValidation.Id(exportId):D}", null, Access(accessToken), 200, ct);

    public Task<AccountExportDownload> DownloadExportAsync(string accessToken, Guid exportId, CancellationToken ct = default)
        => GetFileAsync($"account/exports/{AccountValidation.Id(exportId):D}/download", Access(accessToken), ct);

    public Task<DeleteAccountResponse> DeleteAccountAsync(string accessToken, ProofRequest request, CancellationToken ct = default)
        => SendAsync<DeleteAccountResponse>(HttpMethod.Delete, "account", Required(request), Access(accessToken), 202, ct);

    public Task RequestPasswordResetAsync(PasswordResetRequest request, CancellationToken ct = default)
        => SendAsync<AccountEmptyObject>(HttpMethod.Post, "auth/password-reset/request", Required(request), null, 202, ct);

    public Task ConfirmPasswordResetAsync(PasswordResetConfirmRequest request, CancellationToken ct = default)
        => SendAsync<object>(HttpMethod.Post, "auth/password-reset/confirm", Required(request), null, 204, ct);

    public Task<ReauthResponse> ReauthenticateAsync(string accessToken, PasswordProofRequest request, CancellationToken ct = default)
        => SendAsync<ReauthResponse>(HttpMethod.Post, "account/reauthenticate", Required(request), Access(accessToken), 200, ct);
}
