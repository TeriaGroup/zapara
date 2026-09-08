using Microsoft.AspNetCore.WebUtilities;
using System.Net;
using System.Text;
using Xunit;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Server.Accounts;
using Zapara.Server.Accounts.ExternalProviders;

namespace Zapara.Server.Tests;

public sealed class OAuthCompletionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verified_callback_requires_native_and_browser_proofs_then_issues_once(bool vk)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        using var handler = new OAuthHandler(vk);
        using var http = new HttpClient(handler);
        var provider = vk ? "vk" : "yandex";
        var callback = "https://example.invalid/registered/" + provider;
        using IExternalProviderAdapter adapter = vk ? new VkIdAdapter(new("synthetic-client", callback), http)
            : new YandexIdAdapter(new("synthetic-client", callback), http);
        var registry = new ExternalProviderRegistry([adapter], new Dictionary<string, string> { [provider] = callback });
        var service = new ExternalAuthService(db.DataSource, db.Configuration, TimeProvider.System, registry);
        var verifier = ExternalSecrets.Random();
        var start = await service.StartAsync(provider, new("login", WebEncoders.Base64UrlEncode(ExternalSecrets.Hash(verifier)),
            "S256", new(Guid.NewGuid(), "Тест", "windows"), new("windows", 45001)), ct: TestContext.Current.CancellationToken);
        var state = QueryHelpers.ParseQuery(new Uri(start.AuthorizeUrl).Query)["state"].ToString();
        var error = await Record.ExceptionAsync(async () =>
        {
            var returned = await service.CallbackAsync(provider, new(callback), state, "synthetic-code", vk ? "synthetic-device" : null, ct: TestContext.Current.CancellationToken);
            Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.users"));
            var handoff = QueryHelpers.ParseQuery(returned.Query)["handoffCode"].ToString();
            var request = new ExternalExchangeRequest(start.TransactionId, verifier, handoff);
            await Assert.ThrowsAsync<ExternalAuthException>(() => service.ExchangeAsync(request with { HandoffCode = ExternalSecrets.Random() }, ct: TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ExternalAuthException>(() => service.ExchangeAsync(request with { NativeVerifier = ExternalSecrets.Random() }, ct: TestContext.Current.CancellationToken));
            var result = await service.ExchangeAsync(request, ct: TestContext.Current.CancellationToken);
            Assert.NotNull(result.Session);
            Assert.NotNull(await new AccountService(db.DataSource, db.Configuration, TimeProvider.System).AuthenticateAsync(result.Session.AccessToken, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ExternalAuthException>(() => service.ExchangeAsync(request, ct: TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ExternalAuthException>(() => service.CallbackAsync(provider, new(callback), state, "synthetic-code", vk ? "synthetic-device" : null, ct: TestContext.Current.CancellationToken));
            Assert.Equal(2, handler.Count);
            Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.password_credentials"));
        });
        Assert.Null(error);
    }
}

internal sealed class OAuthHandler(bool vk) : HttpMessageHandler
{
    internal int Count;
    internal string Subject = "synthetic-subject";
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Count);
        var token = request.RequestUri!.AbsolutePath is "/token" or "/oauth2/auth";
        var subject = System.Text.Json.JsonSerializer.Serialize(Subject);
        var payload = token ? "{\"access_token\":\"synthetic-token\"}" : vk
            ? "{\"user\":{\"user_id\":" + subject + ",\"first_name\":\"Тест\"}}"
            : "{\"id\":" + subject + ",\"client_id\":\"synthetic-client\",\"display_name\":\"Тест\",\"default_email\":\"same@example.invalid\"}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") });
    }
}
