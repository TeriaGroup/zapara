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
    public async Task StartingAndCancellingExternalLoginPreservesCurrentBrowserSession()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        using var adapter = new YandexIdAdapter(new("synthetic-client", "https://example.invalid/registered/yandex"));
        var registry = new ExternalProviderRegistry([adapter], new Dictionary<string, string>
            { ["yandex"] = "https://example.invalid/registered/yandex" });
        await using var host = new WebAccountHost(db, configureServices: services =>
        {
            services.RemoveAll<ExternalProviderRegistry>();
            services.AddSingleton(registry);
        }, origin: "https://example.invalid");
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("oauth_existing", WebAccountHost.Password));
        await host.Login("oauth_existing");
        var family = host.Family!;

        var started = await host.Send("POST", "/auth/external/yandex/start", 200, new { purpose = "login" });
        var beforeCancel = await host.Send("GET", "/account/me", 200);
        Assert.Equal(family, beforeCancel.GetProperty("familyId").GetString());

        await host.Send("POST", "/auth/external/" + started.GetProperty("transactionId").GetString() + "/cancel", 204);
        var afterCancel = await host.Send("GET", "/account/me", 200);
        Assert.Equal(family, afterCancel.GetProperty("familyId").GetString());
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.session_families WHERE family_id='{family}' AND revoked_at IS NOT NULL"));
    }

    [Fact]
    public async Task FailedExternalExchangePreservesCurrentBrowserSession()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        var accounts = new AccountService(db.DataSource, db.Configuration, TimeProvider.System);
        await accounts.RegisterAsync(new RegisterRequest("oauth_existing", WebAccountHost.Password), Ct);
        using var providerHttp = new HttpClient(new OAuthHandler(false));
        using var adapter = new YandexIdAdapter(new("synthetic-client", "https://example.invalid/registered/yandex"), providerHttp);
        var registry = new ExternalProviderRegistry([adapter], new Dictionary<string, string>
            { ["yandex"] = "https://example.invalid/registered/yandex" });
        await using var host = new WebAccountHost(db, environment: "Production", registration: false,
            configureServices: services =>
            {
                services.RemoveAll<ExternalProviderRegistry>();
                services.AddSingleton(registry);
            }, origin: "https://example.invalid");
        await host.Bootstrap();
        await host.Login("oauth_existing");
        var family = host.Family!;
        var started = await host.Send("POST", "/auth/external/yandex/start", 200, new { purpose = "login" });
        var state = QueryHelpers.ParseQuery(new Uri(started.GetProperty("authorizeUrl").GetString()!).Query)["state"].ToString();
        var callback = QueryHelpers.AddQueryString("/registered/yandex", new Dictionary<string, string?>
            { ["state"] = state, ["code"] = "synthetic-code" });

        using var response = await host.Client.GetAsync(callback, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var current = await host.Send("GET", "/account/me", 200);
        Assert.Equal(family, current.GetProperty("familyId").GetString());
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.session_families WHERE family_id='{family}' AND revoked_at IS NOT NULL"));
    }

    [Fact]
    public async Task SuccessfulExternalLoginSwitchesBrowserFamily()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        using var providerHttp = new HttpClient(new OAuthHandler(false));
        using var adapter = new YandexIdAdapter(new("synthetic-client", "https://example.invalid/registered/yandex"), providerHttp);
        var registry = new ExternalProviderRegistry([adapter], new Dictionary<string, string>
            { ["yandex"] = "https://example.invalid/registered/yandex" });
        await using var host = new WebAccountHost(db, configureServices: services =>
        {
            services.RemoveAll<ExternalProviderRegistry>();
            services.AddSingleton(registry);
        }, origin: "https://example.invalid");
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("oauth_existing", WebAccountHost.Password));
        await host.Login("oauth_existing");
        var oldFamily = host.Family!;
        var started = await host.Send("POST", "/auth/external/yandex/start", 200, new { purpose = "login" });
        var state = QueryHelpers.ParseQuery(new Uri(started.GetProperty("authorizeUrl").GetString()!).Query)["state"].ToString();
        var callback = QueryHelpers.AddQueryString("/registered/yandex", new Dictionary<string, string?>
            { ["state"] = state, ["code"] = "synthetic-code" });

        using var response = await host.Client.GetAsync(callback, Ct);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var current = await host.Bootstrap();
        Assert.True(current.GetProperty("authenticated").GetBoolean());
        Assert.NotEqual(oldFamily, current.GetProperty("familyId").GetString());
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.session_families WHERE family_id='{oldFamily}' AND revoked_at IS NOT NULL"));
    }

    [Fact]
    public async Task DelayedExternalCallbackCannotReplaceNewerBrowserLogin()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        using var providerGate = new GatedYandexHandler();
        using var providerHttp = new HttpClient(providerGate);
        using var adapter = new YandexIdAdapter(new("synthetic-client", "https://example.invalid/registered/yandex"), providerHttp);
        var registry = new ExternalProviderRegistry([adapter], new Dictionary<string, string>
            { ["yandex"] = "https://example.invalid/registered/yandex" });
        await using var host = new WebAccountHost(db, configureServices: services =>
        {
            services.RemoveAll<ExternalProviderRegistry>();
            services.AddSingleton(registry);
        }, origin: "https://example.invalid");
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("oauth_first", WebAccountHost.Password));
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("oauth_second", WebAccountHost.Password));
        await host.Login("oauth_first");
        var started = await host.Send("POST", "/auth/external/yandex/start", 200, new { purpose = "login" });
        var state = QueryHelpers.ParseQuery(new Uri(started.GetProperty("authorizeUrl").GetString()!).Query)["state"].ToString();
        var callback = QueryHelpers.AddQueryString("/registered/yandex", new Dictionary<string, string?>
            { ["state"] = state, ["code"] = "synthetic-code" });
        var pending = host.Client.GetAsync(callback, Ct);
        await providerGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        try { await host.Login("oauth_second"); }
        finally { providerGate.Release.TrySetResult(); }

        using var response = await pending;
        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        var current = await host.Bootstrap();
        Assert.Equal("oauth_second", current.GetProperty("user").GetProperty("username").GetString());
        Assert.Equal(2L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.users"));
    }

    [Fact]
    public async Task DelayedGuestCallbackCannotReplaceNewerBrowserLogin()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        using var providerGate = new GatedYandexHandler();
        using var providerHttp = new HttpClient(providerGate);
        using var adapter = new YandexIdAdapter(new("synthetic-client", "https://example.invalid/registered/yandex"), providerHttp);
        var registry = new ExternalProviderRegistry([adapter], new Dictionary<string, string>
            { ["yandex"] = "https://example.invalid/registered/yandex" });
        await using var host = new WebAccountHost(db, configureServices: services =>
        {
            services.RemoveAll<ExternalProviderRegistry>();
            services.AddSingleton(registry);
        }, origin: "https://example.invalid");
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("oauth_password", WebAccountHost.Password));
        var started = await host.Send("POST", "/auth/external/yandex/start", 200, new { purpose = "login" });
        var state = QueryHelpers.ParseQuery(new Uri(started.GetProperty("authorizeUrl").GetString()!).Query)["state"].ToString();
        var callback = QueryHelpers.AddQueryString("/registered/yandex", new Dictionary<string, string?>
            { ["state"] = state, ["code"] = "synthetic-code" });
        var pending = host.Client.GetAsync(callback, Ct);
        await providerGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        try { await host.Login("oauth_password"); }
        finally { providerGate.Release.TrySetResult(); }

        using var response = await pending;
        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        var current = await host.Bootstrap();
        Assert.Equal("oauth_password", current.GetProperty("user").GetProperty("username").GetString());
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.users"));
    }

    [Fact]
    public async Task FirstCompletedGuestCallbackInvalidatesConcurrentLoginAttempt()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        using var providerGate = new GatedYandexHandler();
        using var providerHttp = new HttpClient(providerGate);
        using var adapter = new YandexIdAdapter(new("synthetic-client", "https://example.invalid/registered/yandex"), providerHttp);
        var registry = new ExternalProviderRegistry([adapter], new Dictionary<string, string>
            { ["yandex"] = "https://example.invalid/registered/yandex" });
        await using var host = new WebAccountHost(db, configureServices: services =>
        {
            services.RemoveAll<ExternalProviderRegistry>();
            services.AddSingleton(registry);
        }, origin: "https://example.invalid");
        await host.Bootstrap();
        var first = await host.Send("POST", "/auth/external/yandex/start", 200, new { purpose = "login" });
        var second = await host.Send("POST", "/auth/external/yandex/start", 200, new { purpose = "login" });
        static string Callback(System.Text.Json.JsonElement start)
        {
            var state = QueryHelpers.ParseQuery(new Uri(start.GetProperty("authorizeUrl").GetString()!).Query)["state"].ToString();
            return QueryHelpers.AddQueryString("/registered/yandex", new Dictionary<string, string?>
                { ["state"] = state, ["code"] = "synthetic-code" });
        }
        var blocked = host.Client.GetAsync(Callback(second), Ct);
        await providerGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        try
        {
            using var accepted = await host.Client.GetAsync(Callback(first), Ct);
            Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        }
        finally { providerGate.Release.TrySetResult(); }
        var current = await host.Bootstrap();
        var winningFamily = current.GetProperty("familyId").GetString();

        using var rejected = await blocked;
        Assert.NotEqual(HttpStatusCode.Redirect, rejected.StatusCode);
        var final = await host.Bootstrap();
        Assert.Equal(winningFamily, final.GetProperty("familyId").GetString());
    }

    [Fact]
    public async Task FailedNewSessionPersistencePreservesCurrentBrowserSession()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        using var providerHttp = new HttpClient(new OAuthHandler(false));
        using var adapter = new YandexIdAdapter(new("synthetic-client", "https://example.invalid/registered/yandex"), providerHttp);
        var registry = new ExternalProviderRegistry([adapter], new Dictionary<string, string>
            { ["yandex"] = "https://example.invalid/registered/yandex" });
        await using var host = new WebAccountHost(db, configureServices: services =>
        {
            services.RemoveAll<ExternalProviderRegistry>();
            services.AddSingleton(registry);
        }, origin: "https://example.invalid");
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("oauth_existing", WebAccountHost.Password));
        await host.Login("oauth_existing");
        var family = host.Family!;
        var started = await host.Send("POST", "/auth/external/yandex/start", 200, new { purpose = "login" });
        var state = QueryHelpers.ParseQuery(new Uri(started.GetProperty("authorizeUrl").GetString()!).Query)["state"].ToString();
        await db.ExecuteAsync($"""
            CREATE FUNCTION {db.QuotedSchema}.reject_web_session_insert() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected web session insert failure'; END $$;
            CREATE TRIGGER reject_web_session_insert BEFORE INSERT ON {db.QuotedSchema}.web_sessions
            FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.reject_web_session_insert()
            """);
        var callback = QueryHelpers.AddQueryString("/registered/yandex", new Dictionary<string, string?>
            { ["state"] = state, ["code"] = "synthetic-code" });

        var failure = await Record.ExceptionAsync(async () =>
        {
            using var response = await host.Client.GetAsync(callback, Ct);
            Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        });
        Assert.True(failure is null or Npgsql.PostgresException);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.oauth_transactions WHERE status='completed'"));
        var current = await host.Send("GET", "/account/me", 200);
        Assert.Equal(family, current.GetProperty("familyId").GetString());
    }

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

internal sealed class GatedYandexHandler : DelegatingHandler
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int tokenCalls;
    internal GatedYandexHandler() => InnerHandler = new OAuthHandler(false);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath == "/token" && Interlocked.Increment(ref tokenCalls) == 1)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
