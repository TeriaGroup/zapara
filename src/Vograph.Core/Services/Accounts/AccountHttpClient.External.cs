using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Accounts.ExternalResponses;

namespace Vograph.Core.Services.Accounts;

public sealed partial class AccountHttpClient
{
    public Task<ExternalStartResponse> StartExternalAsync(string provider, ExternalStartRequest request,
        string? accessToken = null, CancellationToken ct = default)
        => SendAsync<ExternalStartResponse>(HttpMethod.Post, "auth/external/" + Provider(provider) + "/start",
            Required(request), OptionalAccess(accessToken), 200, ct);

    public async Task<ExternalExchangeResponse> ExchangeExternalAsync(ExternalExchangeRequest request,
        string? accessToken = null, CancellationToken ct = default)
    {
        var result = await SendAsync<ExternalExchangeResponse>(HttpMethod.Post, "auth/external/exchange",
            Required(request), OptionalAccess(accessToken), 200, ct).ConfigureAwait(false);
        if (result.Session is not null) AccountResponseReader.ValidateSession(result.Session);
        return result;
    }

    public Task<ExternalStatusResponse> GetExternalStatusAsync(Guid transactionId, CancellationToken ct = default)
        => SendAsync<ExternalStatusResponse>(HttpMethod.Get,
            $"auth/external/{AccountValidation.Id(transactionId):D}/status", null, null, 200, ct);

    public async Task<IReadOnlyList<ExternalIdentityResponse>> ListIdentitiesAsync(string accessToken, CancellationToken ct = default)
    {
        var items = await SendAsync<ExternalIdentityResponse[]>(HttpMethod.Get, "account/identities", null,
            Access(accessToken), 200, ct).ConfigureAwait(false);
        if (items is null || items.Any(item => item is null || item.Provider is not ("vk" or "yandex")))
            throw new AccountClientException(AccountClientFailure.InvalidPayload);
        return items;
    }

    public Task UnlinkIdentityAsync(string accessToken, string provider, ProofRequest request, CancellationToken ct = default)
        => SendAsync<object>(HttpMethod.Delete, "account/identities/" + Provider(provider), Required(request),
            Access(accessToken), 204, ct);

    private static string Provider(string? provider)
        => provider is "vk" or "yandex" ? provider : throw new AccountClientException(AccountClientFailure.InvalidRequest);

    private static string? OptionalAccess(string? value) => value is null ? null : Access(value);
}
