using System.Text.Json;
using Vograph.Core.Services.Accounts;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalResponses;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public partial class AccountClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capabilities_use_typed_anonymous_root_preserving_GET(bool registration)
    {
        var expected = new AuthCapabilitiesResponse(true, false, false, registration, false);
        using var handler = new AccountClientHandler { Send = (request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://example.invalid/root/api/v1/auth/capabilities", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            Assert.Null(request.Content);
            return Task.FromResult(Json(expected));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/root/"));
        Assert.Equal(expected, await client.GetCapabilitiesAsync(Ct));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("vk")]
    [InlineData("yandex")]
    [InlineData("registration")]
    [InlineData("recovery")]
    public async Task Capabilities_require_every_server_field(string missing)
    {
        var node = JsonSerializer.SerializeToNode(new AuthCapabilitiesResponse(true, false, false, true, false),
            AccountJson.CreateOptions())!.AsObject();
        node.Remove(missing);
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(Raw(node.ToJsonString())) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        var error = await Assert.ThrowsAsync<AccountClientException>(() => client.GetCapabilitiesAsync(Ct));
        Assert.Equal(AccountClientFailure.InvalidPayload, error.Failure);
    }
}
