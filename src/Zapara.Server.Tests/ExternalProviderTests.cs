using Xunit;
using static Zapara.Server.Accounts.ExternalProviders.ExternalProviderTestsSupport;

namespace Zapara.Server.Accounts.ExternalProviders;

public sealed class ExternalProviderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Authorization_is_fixed_minimal_S256_and_does_not_send(bool vk)
    {
        using var handler = new ExternalProviderTestsHandler();
        using var http = new HttpClient(handler);
        using var adapter = Adapter(vk, http);
        Uri? uri = null;
        Assert.Null(Record.Exception(() => { uri = adapter.BuildAuthorizationUri(State, Challenge); }));
        Assert.NotNull(uri);
        Assert.Equal(vk ? "https://id.vk.ru/authorize" : "https://oauth.yandex.ru/authorize", uri.GetLeftPart(UriPartial.Path));
        Assert.Equal(new Dictionary<string, string>
        {
            ["response_type"] = "code", ["client_id"] = "example-id", ["redirect_uri"] = Callback,
            ["state"] = new string('s', 32) + "_-A", ["code_challenge_method"] = "S256",
            ["code_challenge"] = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(new string('v', 43)))),
            ["scope"] = vk ? "vkid.personal_info" : "login:info"
        }, Form(uri.Query));
        Assert.DoesNotContain(Secret, uri.AbsoluteUri);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Exchange_uses_correct_forms_and_returns_only_verified_minimal_identity(bool vk, bool secret)
    {
        using var handler = new ExternalProviderTestsHandler();
        if (vk)
        {
            handler.TokenJson = "{\"access_token\":\"ACCESS_CANARY\",\"user_id\":\"opaque-subject\"}";
            handler.UserJson = "{\"user\":{\"user_id\":\"opaque-subject\",\"first_name\":\"Test\",\"email\":\"discard@example.invalid\",\"phone\":\"discard\"}}";
        }
        using var http = new HttpClient(handler);
        using var adapter = Adapter(vk, http, secret: secret);
        VerifiedExternalIdentity? result = null;
        Assert.Null(await Record.ExceptionAsync(async () => { result = await Exchange(adapter, vk); }));
        Assert.NotNull(result);
        Assert.Equal(vk ? "vk" : "yandex", result.Provider);
        Assert.Equal("opaque-subject", result.Subject);
        Assert.Equal("Test", result.DisplayName);
        Assert.Equal(new[] { "DisplayName", "Provider", "Subject" }, typeof(VerifiedExternalIdentity).GetProperties().Select(p => p.Name).Order());
        Assert.Empty(typeof(VerifiedExternalIdentity).GetConstructors());
        Assert.Equal(2, handler.Requests.Count);
        var token = handler.Requests[0];
        Assert.Equal("POST", token.Method);
        Assert.Equal(vk ? "https://id.vk.ru/oauth2/auth" : "https://oauth.yandex.ru/token", token.Uri.AbsoluteUri);
        var expected = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = "example-id", ["code"] = Code,
            ["code_verifier"] = new string('v', 43), ["redirect_uri"] = Callback
        };
        if (vk) { expected["device_id"] = "device+&=/"; expected["state"] = new string('s', 32) + "_-A"; }
        if (secret) expected[vk ? "service_token" : "client_secret"] = Secret;
        Assert.Equal(expected, Form(token.Body));
        Assert.Null(token.Authorization);
        var info = handler.Requests[1];
        Assert.Equal(vk ? "POST" : "GET", info.Method);
        Assert.Equal(vk ? "https://id.vk.ru/oauth2/user_info" : "https://login.yandex.ru/info?format=json", info.Uri.AbsoluteUri);
        if (vk)
        {
            Assert.Equal(new Dictionary<string, string> { ["access_token"] = "ACCESS_CANARY", ["client_id"] = "example-id" }, Form(info.Body));
            Assert.Null(info.Authorization);
        }
        else { Assert.Equal("OAuth ACCESS_CANARY", info.Authorization); Assert.Empty(info.Body); }
        Assert.All(handler.Requests, r => Assert.DoesNotContain("CANARY", r.Uri.AbsoluteUri));
        Assert.All(handler.Contents, c => Assert.True(c.Disposed));
        Assert.DoesNotContain("CANARY", result.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("sssssssssssssssssssssssssssssss+")]
    [InlineData("sssssssssssssssssssssssssssssss\n")]
    public void Invalid_state_format_is_rejected_not_transaction_correlation(string value)
        => Assert.Equal(ExternalProviderFailure.InvalidRequest, Assert.Throws<ExternalProviderException>(() => new ProviderState(value)).Failure);

    [Fact]
    public void Pkce_and_configuration_are_validated_and_redacted_without_network()
    {
        foreach (var value in new[] { "short", new string('v', 129), new string('.', 43), new string('v', 42) + "\n" })
            Assert.Throws<ExternalProviderException>(() => new CodeVerifier(value));
        foreach (var value in new[] { "short", new string('A', 42) + "B", new string('A', 43) + "=" })
            Assert.Throws<ExternalProviderException>(() => new CodeChallenge(value));
        foreach (var uri in new[] { "http://example.invalid/cb", "zapara://cb", "https://user:SECRET_CANARY@example.invalid/cb", "https://example.invalid/cb?return_to=x", "https://example.invalid/cb#x" })
        {
            var error = Assert.Throws<ExternalProviderException>(() => new VkIdOptions("example-id", uri, Secret));
            Assert.Equal(ExternalProviderFailure.InvalidConfiguration, error.Failure);
            Assert.DoesNotContain(Secret, error.ToString());
            Assert.Null(error.InnerException);
        }
        Assert.DoesNotContain(Secret, new VkIdOptions("example-id", Callback, Secret).ToString());
        Assert.DoesNotContain(Secret, new YandexIdOptions("example-id", Callback, Secret).ToString());
        Assert.DoesNotContain(new string('v', 43), Verifier.ToString());
        Assert.Throws<ExternalProviderException>(() => new YandexIdOptions("example-id", null));
        Assert.DoesNotContain(typeof(VkIdOptions).GetProperties(), p => p.Name.Contains("Endpoint") || p.Name.Contains("Url"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unconfigured_and_invalid_arguments_never_send(bool vk)
    {
        using var handler = new ExternalProviderTestsHandler();
        using var http = new HttpClient(handler);
        using IExternalProviderAdapter absent = vk ? new VkIdAdapter(new(), http) : new YandexIdAdapter(new(), http);
        Assert.False(absent.IsConfigured);
        Assert.Equal(ExternalProviderFailure.NotConfigured, Assert.Throws<ExternalProviderException>(() => absent.BuildAuthorizationUri(State, Challenge)).Failure);
        Assert.Equal(ExternalProviderFailure.NotConfigured, (await Assert.ThrowsAsync<ExternalProviderException>(() => Exchange(absent, vk))).Failure);
        using var adapter = Adapter(vk, http);
        foreach (var code in new[] { "", new string('c', 8193), "code\r\nSECRET_CANARY" })
            Assert.Equal(ExternalProviderFailure.InvalidRequest, (await Assert.ThrowsAsync<ExternalProviderException>(() => adapter.ExchangeIdentityAsync(code, State, Verifier, vk ? "device" : null, TestContext.Current.CancellationToken))).Failure);
        if (vk) await Assert.ThrowsAsync<ExternalProviderException>(() => adapter.ExchangeIdentityAsync(Code, State, Verifier, ct: TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }
}
