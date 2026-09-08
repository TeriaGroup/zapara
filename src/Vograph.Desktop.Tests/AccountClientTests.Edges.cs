using System.Net;
using System.Text.Json;
using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public partial class AccountClientTests
{
    [Fact]
    public async Task Internal_error_preserves_only_the_fixed_contract_code()
    {
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(
            Json(new { title = Password, status = 500, code = "internal_error" }, HttpStatusCode.InternalServerError)) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        var error = await Assert.ThrowsAsync<AccountClientException>(() => client.LoginAsync(Login, Ct));
        Assert.Equal("internal_error", error.Code);
        Assert.Equal(500, error.Status);
        Assert.DoesNotContain(Password, error.ToString());
    }

    [Fact]
    public async Task Device_pagination_preserves_every_field_and_cursor()
    {
        var cursor = new string('A', 55);
        var device = new DeviceResponse(FamilyId, DeviceId, "Windows", "windows", Now, Now, Now.AddDays(30), true);
        using var handler = new AccountClientHandler { Send = (request, _) =>
        {
            Assert.Equal("?limit=100&cursor=" + cursor, request.RequestUri!.Query);
            return Task.FromResult(Json(new DevicesResponse([device], cursor)));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/prefix"));
        var page = await client.ListDevicesAsync(Token("za_"), 100, cursor, Ct);
        Assert.Equal(device, Assert.Single(page.Devices));
        Assert.Equal(cursor, page.NextCursor);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"devices\":[]}")]
    [InlineData("{\"devices\":null,\"nextCursor\":null}")]
    [InlineData("{\"devices\":[{}],\"nextCursor\":null}")]
    public async Task Device_payload_requires_all_fields(string body)
    {
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(Raw(body)) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        Assert.Equal(AccountClientFailure.InvalidPayload, (await Assert.ThrowsAsync<AccountClientException>(
            () => client.ListDevicesAsync(Token("za_"), ct: Ct))).Failure);
    }

    [Fact]
    public async Task Request_shape_guards_prevent_network_and_default_headers_are_not_accepted()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.RefreshAsync(Token("za_"), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetMeAsync("invalid", Ct));
        await Assert.ThrowsAsync<AccountClientException>(() => client.ListDevicesAsync(Token("za_"), 101, ct: Ct));
        await Assert.ThrowsAsync<AccountClientException>(() => client.ListDevicesAsync(Token("za_"), cursor: "?invalid", ct: Ct));
        http.DefaultRequestHeaders.Authorization = new("Bearer", "unrelated");
        Assert.Throws<ArgumentException>(() => new AccountHttpClient(http, new("https://example.invalid/")));
        await Assert.ThrowsAsync<AccountClientException>(() => client.LoginAsync(Login, Ct));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("http://localhost:1234/prefix")]
    [InlineData("http://127.0.0.2:1234/prefix")]
    [InlineData("http://[::1]:1234/prefix")]
    [InlineData("https://example.invalid/prefix")]
    public void Scope_canonicalizes_root_and_separates_environments(string url)
    {
        var scope = new AccountServerScope(new(url));
        Assert.Equal(scope.Key, new AccountServerScope(new(url + "/")).Key);
        Assert.NotEqual(scope.Key, new AccountServerScope(new(url + "-other")).Key);
        Assert.Equal(64, scope.Key.Length);
        using var owned = AccountHttpClient.CreateOwned(new(url));
        Assert.Equal(scope.Key, owned.Scope.Key);
    }

    [Fact]
    public async Task Injected_client_survives_adapter_disposal_and_session_strings_are_redacted()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        new AccountHttpClient(http, new("https://example.invalid/")).Dispose();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/");
        using var response = await http.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var text in new[] { Session().ToString(), Login.ToString(), new RefreshRequest(Token("zr_")).ToString(),
            new RegisterRequest("Test.User", Password).ToString(), new ChangePasswordRequest(Password, Password).ToString() })
        {
            Assert.DoesNotContain(Password, text);
            Assert.DoesNotContain(Token("zr_"), text);
            Assert.DoesNotContain(Token("za_"), text);
        }
    }

    [Theory]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(503)]
    public async Task Redirect_or_unavailable_POST_is_not_replayed(int status)
    {
        using var handler = new AccountClientHandler { Send = (_, _) =>
        {
            var response = Raw("", (HttpStatusCode)status);
            response.Headers.Location = new("https://other.invalid/" + Password);
            return Task.FromResult(response);
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        var error = await Assert.ThrowsAsync<AccountClientException>(() => client.UpdateProfileAsync(Token("za_"), new(null), Ct));
        Assert.DoesNotContain(Password, error.ToString());
        Assert.Equal(1, handler.Calls);
    }
}
