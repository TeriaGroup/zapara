using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Accounts.ExternalResponses;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.OAuthTestHarness;

namespace Zapara.Server.Tests;

public sealed class OAuthHttpTests
{
    [Fact]
    public async Task Unconfigured_capabilities_and_start_are_truthful()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        var caps = await host.Send("GET", "/auth/capabilities", 200);
        Assert.False(caps.GetProperty("vk").GetBoolean());
        Assert.False(caps.GetProperty("yandex").GetBoolean());
        Assert.True(caps.GetProperty("recovery").GetBoolean());
        await host.Send("POST", "/auth/external/yandex/start", 503, new { }, code: "provider_unavailable");
    }

    [Fact]
    public async Task Controlled_http_callback_redirect_and_post_exchange_keep_sessions_out_of_browser()
    {
        await using var h = await Create();
        await using var host = new AccountApiTestHost(h.Db, clock: h.Clock);
        await using var factory = host.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ExternalProviderRegistry>(); services.AddSingleton(h.Registry);
            services.RemoveAll<ExternalAuthService>(); services.AddSingleton(h.Service);
        }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://example.invalid"), AllowAutoRedirect = false });
        var verifier = ExternalSecrets.Random();
        using var startResponse = await client.PostAsJsonAsync("/api/v1/auth/external/yandex/start", new ExternalStartRequest("login",
            WebEncoders.Base64UrlEncode(ExternalSecrets.Hash(verifier)), "S256", new(Guid.NewGuid(), "Тест", "windows"), new("windows", 45001)), AccountJson.CreateOptions(), Ct);
        Assert.Equal(200, (int)startResponse.StatusCode);
        var start = (await startResponse.Content.ReadFromJsonAsync<ExternalStartResponse>(AccountJson.CreateOptions(), Ct))!;
        var state = QueryHelpers.ParseQuery(new Uri(start.AuthorizeUrl).Query)["state"].ToString();
        using var callback = await client.GetAsync(QueryHelpers.AddQueryString(Callback, new Dictionary<string, string?> { ["state"] = state, ["code"] = "synthetic-code" }), Ct);
        Assert.Equal(302, (int)callback.StatusCode);
        Assert.True(callback.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", Assert.Single(callback.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("default-src 'none'", Assert.Single(callback.Headers.GetValues("Content-Security-Policy")));
        var query = QueryHelpers.ParseQuery(callback.Headers.Location!.Query);
        Assert.Equal(2, query.Count);
        using var exchange = await client.PostAsJsonAsync("/api/v1/auth/external/exchange", new ExternalExchangeRequest(start.TransactionId, verifier, query["handoffCode"].ToString()), AccountJson.CreateOptions(), Ct);
        Assert.Equal(200, (int)exchange.StatusCode);
        var result = (await exchange.Content.ReadFromJsonAsync<ExternalExchangeResponse>(AccountJson.CreateOptions(), Ct))!;
        Assert.NotNull(result.Session);
        Assert.DoesNotContain(host.Logs, s => s.Contains(result.Session.AccessToken, StringComparison.Ordinal));
    }
}
