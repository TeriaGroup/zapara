using System.Net;
using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Accounts.ExternalResponses;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public partial class AccountClientTests
{
    [Theory]
    [InlineData("vk")]
    [InlineData("yandex")]
    public async Task Unconfigured_external_provider_maps_503(string provider)
    {
        using var handler = new AccountClientHandler { Send = (request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"https://example.invalid/api/v1/auth/external/{provider}/start", request.RequestUri!.AbsoluteUri);
            Assert.StartsWith("https://example.invalid/", request.RequestUri.AbsoluteUri, StringComparison.Ordinal);
            return Task.FromResult(Json(new { title = "Недоступен", status = 503, code = "provider_unavailable" },
                HttpStatusCode.ServiceUnavailable));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        var error = await Assert.ThrowsAsync<AccountClientException>(() => client.StartExternalAsync(provider, ExternalStart, ct: Ct));
        Assert.Equal(AccountClientFailure.ProviderUnavailable, error.Failure);
        Assert.Equal(503, error.Status);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task External_start_sends_optional_bearer_only_when_supplied()
    {
        var start = new ExternalStartResponse(TransactionId, "https://example.invalid/mock/authorize", Now.AddMinutes(10));
        using var handler = new AccountClientHandler { Send = (request, _) =>
        {
            Assert.Equal("Bearer " + Token("za_"), request.Headers.Authorization?.ToString());
            Assert.Equal("https://example.invalid/api/v1/auth/external/yandex/start", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Json(start));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        Assert.Equal(start, await client.StartExternalAsync("yandex", ExternalStart, Token("za_"), Ct));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Invalid_external_proof_on_unlink_is_403()
    {
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(
            Json(new { title = Password, status = 403, code = "invalid_external_proof" }, HttpStatusCode.Forbidden)) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        var error = await Assert.ThrowsAsync<AccountClientException>(
            () => client.UnlinkIdentityAsync(Token("za_"), "yandex", Proof, Ct));
        Assert.Equal(AccountClientFailure.InvalidExternalProof, error.Failure);
        Assert.Equal(403, error.Status);
        Assert.DoesNotContain(Password, error.ToString());
        Assert.DoesNotContain(Proof.ProofToken, error.ToString());
    }

    [Fact]
    public async Task External_exchange_accepts_completed_session()
    {
        var request = new ExternalExchangeRequest(TransactionId, "verifier", new string('D', 43));
        var response = new ExternalExchangeResponse("completed", Session());
        using var handler = new AccountClientHandler { Send = (httpRequest, _) =>
        {
            Assert.Equal(HttpMethod.Post, httpRequest.Method);
            Assert.Equal("https://example.invalid/api/v1/auth/external/exchange", httpRequest.RequestUri!.AbsoluteUri);
            Assert.Null(httpRequest.Headers.Authorization);
            return Task.FromResult(Json(response));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        Assert.Equal(response, await client.ExchangeExternalAsync(request, ct: Ct));
    }
}
