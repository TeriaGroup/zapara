using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Server.Accounts;
using Zapara.Server.Accounts.ExternalProviders;

namespace Zapara.Server.Tests;

public sealed class WebOAuthTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BrowserCallbackRequiresInitiatingCookieAndCannotReplayOrRedirectElsewhere()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        using var providerHttp = new HttpClient(new OAuthHandler(false));
        using var adapter = new YandexIdAdapter(new("synthetic-client", "https://example.invalid/registered/yandex"), providerHttp);
        var registry = new ExternalProviderRegistry([adapter], new Dictionary<string, string> { ["yandex"] = "https://example.invalid/registered/yandex" });
        await using var host = new WebAccountHost(db, configureServices: services =>
        {
            services.RemoveAll<ExternalProviderRegistry>();
            services.AddSingleton(registry);
        }, origin: "https://example.invalid");
        await host.Bootstrap();
        await host.Send("POST", "/auth/external/yandex/start", 400, new { purpose = "login", returnUrl = "https://evil.invalid" });
        var started = await host.Send("POST", "/auth/external/yandex/start", 200, new { purpose = "login" });
        var authorize = new Uri(started.GetProperty("authorizeUrl").GetString()!);
        var state = QueryHelpers.ParseQuery(authorize.Query)["state"].ToString();
        var resultPath = "/auth/external/" + started.GetProperty("transactionId").GetString() + "/result";
        var pending = await host.Send("GET", resultPath, 200);
        Assert.Equal("pending", pending.GetProperty("status").GetString());
        var path = QueryHelpers.AddQueryString("/registered/yandex", new Dictionary<string, string?> { ["state"] = state, ["code"] = "synthetic-code" });
        using var stranger = host.Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://example.invalid"), AllowAutoRedirect = false });
        using var wrongBrowser = await stranger.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, wrongBrowser.StatusCode);
        using var accepted = await host.Client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Equal("/app/settings", accepted.Headers.Location!.OriginalString);
        Assert.DoesNotContain("handoffCode", accepted.Headers.Location.OriginalString);
        var body = await host.Bootstrap();
        Assert.True(body.GetProperty("authenticated").GetBoolean());
        var completed = await host.Send("GET", resultPath, 200);
        Assert.Equal("completed", completed.GetProperty("status").GetString());
        Assert.DoesNotContain("accessToken", completed.GetRawText());
        using var replay = await host.Client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.Gone, replay.StatusCode);
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.session_families WHERE platform='web'"));
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.web_sessions"));
        var devices = await host.Send("GET", "/account/devices", 200);
        Assert.Equal("web", devices.GetProperty("devices")[0].GetProperty("platform").GetString());
    }

    [Fact]
    public async Task StaleBrowserCookieDoesNotBlockExternalLogin()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        using var providerHttp = new HttpClient(new OAuthHandler(false));
        using var adapter = new YandexIdAdapter(new("synthetic-client", "https://example.invalid/registered/yandex"), providerHttp);
        var registry = new ExternalProviderRegistry([adapter], new Dictionary<string, string> { ["yandex"] = "https://example.invalid/registered/yandex" });
        await using var host = new WebAccountHost(db, configureServices: services =>
        {
            services.RemoveAll<ExternalProviderRegistry>();
            services.AddSingleton(registry);
        }, origin: "https://example.invalid");
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("oauth_stale", WebAccountHost.Password));
        await host.Login("oauth_stale");
        await db.ExecuteAsync($"DELETE FROM {db.QuotedSchema}.web_sessions");
        var started = await host.Send("POST", "/auth/external/yandex/start", 200, new { purpose = "login" });
        Assert.False(string.IsNullOrEmpty(started.GetProperty("authorizeUrl").GetString()));
    }

    [Fact]
    public async Task NativeOAuthStillRejectsBrowserReturnKind()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        using var providerHttp = new HttpClient(new OAuthHandler(false));
        using var adapter = new YandexIdAdapter(new("synthetic-client", "https://example.invalid/registered/yandex"), providerHttp);
        var registry = new ExternalProviderRegistry([adapter], new Dictionary<string, string> { ["yandex"] = "https://example.invalid/registered/yandex" });
        await using var host = new WebAccountHost(db, configureServices: services =>
        {
            services.RemoveAll<ExternalProviderRegistry>();
            services.AddSingleton(registry);
        }, origin: "https://example.invalid");
        using var rejected = await host.Client.PostAsJsonAsync("/api/v1/auth/external/yandex/start", new ExternalStartRequest("login",
            WebEncoders.Base64UrlEncode(new byte[32]), "S256", new(Guid.NewGuid(), "Браузер", "web"), new("web")), AccountJson.CreateOptions(), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.oauth_transactions"));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
