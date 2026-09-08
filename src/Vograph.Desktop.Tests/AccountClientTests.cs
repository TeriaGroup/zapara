using System.Net;
using System.Text.Json;
using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public partial class AccountClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_W1_operation_uses_exact_contract_and_per_request_bearer()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/root"), new AccountClientClock());
        var expected = new Queue<(string Method, string Path, object? Body, bool Auth, object? Response, HttpStatusCode Status)>([
            ("POST", "auth/register", new RegisterRequest("Test.User", Password), false, User, HttpStatusCode.Created),
            ("POST", "auth/login", Login, false, Session(), HttpStatusCode.OK),
            ("POST", "auth/refresh", new RefreshRequest(Token("zr_")), false, Session(2), HttpStatusCode.OK),
            ("GET", "account/me", null, true, new MeResponse(User, FamilyId, ["password"]), HttpStatusCode.OK),
            ("PATCH", "account/me", new UpdateProfileRequest(null), true, User, HttpStatusCode.OK),
            ("GET", "account/devices?limit=20", null, true, new DevicesResponse([], null), HttpStatusCode.OK),
            ("DELETE", $"account/devices/{FamilyId:D}", null, true, null, HttpStatusCode.NoContent),
            ("POST", "account/sessions/revoke-all", null, true, null, HttpStatusCode.NoContent),
            ("POST", "auth/logout", null, true, null, HttpStatusCode.NoContent),
            ("POST", "account/password/change", new ChangePasswordRequest(Password, Password + "new"), true, null, HttpStatusCode.NoContent)
        ]);
        handler.Send = async (request, ct) =>
        {
            var e = expected.Dequeue();
            Assert.Equal(e.Method, request.Method.Method);
            Assert.Equal("https://example.invalid/root/api/v1/" + e.Path, request.RequestUri!.AbsoluteUri);
            Assert.Equal(e.Auth ? "Bearer " + Token("za_") : null, request.Headers.Authorization?.ToString());
            Assert.Null(http.DefaultRequestHeaders.Authorization);
            Assert.Single(request.Headers.Accept, h => h.MediaType == "application/json");
            if (e.Body is null) Assert.Null(request.Content);
            else
            {
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
                Assert.Equal(JsonSerializer.Serialize(e.Body, AccountJson.CreateOptions()), await request.Content.ReadAsStringAsync(ct));
            }
            return e.Response is null ? new(e.Status) : Json(e.Response, e.Status);
        };
        Assert.Equal(User, await client.RegisterAsync(new("Test.User", Password), Ct));
        Assert.Equal(Session(), await client.LoginAsync(Login, Ct));
        Assert.Equal(Session(2), await client.RefreshAsync(Token("zr_"), Ct));
        Assert.Equal(FamilyId, (await client.GetMeAsync(Token("za_"), Ct)).FamilyId);
        await client.UpdateProfileAsync(Token("za_"), new(null), Ct);
        await client.ListDevicesAsync(Token("za_"), ct: Ct);
        await client.RevokeSessionAsync(Token("za_"), FamilyId, Ct);
        await client.RevokeAllAsync(Token("za_"), Ct);
        await client.LogoutAsync(Token("za_"), Ct);
        await client.ChangePasswordAsync(Token("za_"), new(Password, Password + "new"), Ct);
        Assert.Empty(expected);
    }

    [Theory]
    [InlineData(401, "invalid_session", AccountClientFailure.InvalidSession)]
    [InlineData(400, "invalid_request", AccountClientFailure.InvalidRequest)]
    [InlineData(401, "invalid_credentials", AccountClientFailure.InvalidCredentials)]
    [InlineData(503, "db_unavailable", AccountClientFailure.DbUnavailable)]
    [InlineData(404, "", AccountClientFailure.NotConfigured)]
    [InlineData(404, "session_not_found", AccountClientFailure.SessionNotFound)]
    [InlineData(429, "rate_limited", AccountClientFailure.RateLimited)]
    [InlineData(409, "username_unavailable", AccountClientFailure.UsernameUnavailable)]
    [InlineData(503, "registration_unavailable", AccountClientFailure.RegistrationUnavailable)]
    [InlineData(401, "db_unavailable", AccountClientFailure.ServerUnavailable)]
    public async Task Errors_are_allowlisted_sanitized_and_refresh_is_never_retried(int status, string code, AccountClientFailure failure)
    {
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(code == "" ? new HttpResponseMessage((HttpStatusCode)status) :
            Json(new { title = Password, status, code }, (HttpStatusCode)status)) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        var error = await Assert.ThrowsAsync<AccountClientException>(() => client.RefreshAsync(Token("zr_"), Ct));
        Assert.Equal(failure, error.Failure);
        Assert.Equal(status, error.Status);
        Assert.DoesNotContain(Password, error.ToString());
        Assert.Null(error.InnerException);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("http://example.invalid/")]
    [InlineData("https://user:password@example.invalid/")]
    [InlineData("https://example.invalid/?secret=x")]
    [InlineData("https://example.invalid/#x")]
    [InlineData("/relative")]
    public void Invalid_base_is_rejected_without_echo(string uri)
    {
        using var http = new HttpClient();
        var error = Assert.Throws<ArgumentException>(() => new AccountHttpClient(http, new(uri, UriKind.RelativeOrAbsolute)));
        Assert.DoesNotContain(uri, error.ToString());
    }
}
