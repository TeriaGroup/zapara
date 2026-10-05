using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Zapara.Contracts.Accounts;

namespace Zapara.Server.Tests;

public sealed class WebSessionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task GuestBootstrapWorksWithoutAccountModuleAndRejectsCrossSiteRead()
    {
        await using var host = new WebAccountHost(null);
        using var response = await host.Client.GetAsync("/web-api/session", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await host.Read(response);
        Assert.False(body.GetProperty("authenticated").GetBoolean());
        Assert.False(body.GetProperty("capabilities").GetProperty("password").GetBoolean());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-ZaparaBrowser=", cookie);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        using var cross = new HttpRequestMessage(HttpMethod.Get, "/web-api/session");
        cross.Headers.Add("Sec-Fetch-Site", "cross-site");
        using var denied = await host.Client.SendAsync(cross, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task LoginRequiresCsrfAndOriginAndNeverReturnsBearerTokens()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        await using var host = new WebAccountHost(db);
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("browser_user", WebAccountHost.Password));
        await host.Send("POST", "/auth/login", 400, new { username = "browser_user" });
        await host.Send("POST", "/auth/login", 400, new { username = "browser_user", password = WebAccountHost.Password, deviceName = new string('x', 81) });
        await host.Send("POST", "/auth/login", 403, new { username = "browser_user", password = WebAccountHost.Password }, csrf: false);
        await host.Send("POST", "/auth/login", 403, new { username = "browser_user", password = WebAccountHost.Password }, origin: "https://other.invalid");
        var login = await host.Login("browser_user");
        Assert.True(login.GetProperty("authenticated").GetBoolean());
        var text = login.GetRawText();
        Assert.DoesNotContain("accessToken", text);
        Assert.DoesNotContain("refreshToken", text);
        Assert.DoesNotContain("za_", text);
        Assert.DoesNotContain("zr_", text);
        using var native = await host.Client.GetAsync("/api/v1/account/me", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, native.StatusCode);
        var encrypted = await db.ScalarAsync<string>($"SELECT protected_tokens FROM {db.QuotedSchema}.web_sessions");
        Assert.DoesNotContain("za_", encrypted);
        Assert.DoesNotContain("zr_", encrypted);
        await host.Send("POST", "/auth/logout", 204);
        await host.Send("GET", "/account/me", 401);
    }

    [Fact]
    public async Task ConcurrentRefreshIsAtomicAndOldTabCannotWriteAsNewUser()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        var clock = new AccountClock();
        await using var host = new WebAccountHost(db, clock);
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("browser_first", WebAccountHost.Password));
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("browser_second", WebAccountHost.Password));
        await host.Login("browser_first");
        var first = host.Family;
        clock.Now = clock.Now.AddMinutes(20);
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => host.Send("GET", "/account/me", 200)));
        Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.session_families WHERE revocation_reason='refresh_replay'"));
        await host.Login("browser_second");
        await host.Send("PATCH", "/account/me", 409, new UpdateProfileRequest("Stale tab"), family: first);
        var current = await host.Send("GET", "/account/me", 200);
        Assert.Equal("browser_second", current.GetProperty("user").GetProperty("username").GetString());
        Assert.Equal(JsonValueKind.Null, current.GetProperty("user").GetProperty("displayName").ValueKind);
    }

    [Fact]
    public async Task ProductionRegistrationNeedsExplicitFlag()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        await using (var denied = new WebAccountHost(db, environment: "Production"))
        {
            var state = await denied.Bootstrap();
            Assert.False(state.GetProperty("capabilities").GetProperty("registration").GetBoolean());
            await denied.Send("POST", "/auth/register", 503, new RegisterRequest("production_off", WebAccountHost.Password));
        }
        await using var allowed = new WebAccountHost(db, environment: "Production", registration: true);
        var enabled = await allowed.Bootstrap();
        Assert.True(enabled.GetProperty("capabilities").GetProperty("registration").GetBoolean());
        await allowed.Send("POST", "/auth/register", 201, new RegisterRequest("production_on", WebAccountHost.Password));
    }

    [Fact]
    public async Task DurableSessionSurvivesHostRestartAndNativeRevocationTakesEffect()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        string cookies;
        Guid family;
        await using (var initial = new WebAccountHost(db))
        {
            await initial.Bootstrap();
            await initial.Send("POST", "/auth/register", 201, new RegisterRequest("durable_browser", WebAccountHost.Password));
            await initial.Login("durable_browser");
            family = Guid.Parse(initial.Family!);
            cookies = initial.CookieHeader;
        }
        await using var restarted = new WebAccountHost(db);
        restarted.Client.DefaultRequestHeaders.Add("Cookie", cookies);
        var resumed = await restarted.Bootstrap();
        Assert.True(resumed.GetProperty("authenticated").GetBoolean());
        Assert.Equal(family, resumed.GetProperty("familyId").GetGuid());
        var accounts = restarted.Factory.Services.GetRequiredService<Zapara.Server.Accounts.AccountService>();
        var native = await accounts.LoginAsync(new("durable_browser", WebAccountHost.Password, new(Guid.NewGuid(), "Windows", "windows")), Ct);
        await accounts.RevokeSessionAsync(native.AccessToken, family, Ct);
        await restarted.Send("PATCH", "/account/me", 401, new UpdateProfileRequest("Rejected"));
    }

    [Fact]
    public async Task ExplicitRefreshIsOneShotAndRejectsABodyBrowsersDoNotSend()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        await using var host = new WebAccountHost(db);
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("refresh_browser", WebAccountHost.Password));
        await host.Login("refresh_browser");
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.refresh_tokens WHERE consumed_at IS NOT NULL"));
        var first = await host.Send("POST", "/auth/refresh", 200);
        Assert.DoesNotContain("refreshToken", first.GetRawText());
        Assert.DoesNotContain("accessToken", first.GetRawText());
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.refresh_tokens WHERE consumed_at IS NOT NULL"));
        await host.Send("POST", "/auth/refresh", 200);
        Assert.Equal(2L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.refresh_tokens WHERE consumed_at IS NOT NULL"));
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.session_families WHERE revocation_reason='refresh_replay'"));
        await host.Send("POST", "/auth/refresh", 400, new { refreshToken = "not-sent-by-the-browser" });
        Assert.Equal(2L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.refresh_tokens WHERE consumed_at IS NOT NULL"));
    }

    [Fact]
    public async Task RecoveryStartStaysUnavailableOutsideDevelopment()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        await using var host = new WebAccountHost(db, environment: "Production", registration: true);
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("recovery_off", WebAccountHost.Password));
        await host.Login("recovery_off");
        var problem = await host.Send("POST", "/account/recovery-email/start", 503,
            new StartRecoveryEmailRequest("owner@example.invalid", new string('a', 43)));
        Assert.Equal("recovery_unavailable", problem.GetProperty("code").GetString());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}

internal sealed class WebAccountHost : IAsyncDisposable
{
    internal const string Password = "Synthetic browser password 123!";
    internal HttpClient Client { get; }
    internal WebApplicationFactory<Program> Factory { get; }
    internal string? Csrf { get; private set; }
    internal string? Family { get; private set; }
    private readonly Dictionary<string, string> cookies = new();
    internal string CookieHeader => string.Join("; ", cookies.Values);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    internal WebAccountHost(AccountsPostgresFixture? db, TimeProvider? clock = null, string environment = "Testing", bool? registration = null,
        Action<IServiceCollection>? configureServices = null, string origin = "https://localhost", Dictionary<string, string?>? moduleSettings = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Web:Enabled"] = "true", ["Accounts:Enabled"] = (db is not null).ToString(),
            ["Accounts:Schema"] = db?.Schema, ["ConnectionStrings:Accounts"] = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES"),
            ["Accounts:RegistrationEnabled"] = registration?.ToString()
        };
        if (moduleSettings is not null) foreach (var pair in moduleSettings) settings[pair.Key] = pair.Value;
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Web:Enabled", "true");
            builder.UseSetting("Accounts:Enabled", (db is not null).ToString());
            foreach (var pair in settings.Where(pair => pair.Key.EndsWith(":Enabled", StringComparison.Ordinal))) builder.UseSetting(pair.Key, pair.Value);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            if (clock is not null) builder.ConfigureServices(services => { services.RemoveAll<TimeProvider>(); services.AddSingleton(clock); });
            if (configureServices is not null) builder.ConfigureServices(configureServices);
        });
        Client = Factory.CreateClient(new() { BaseAddress = new Uri(origin), AllowAutoRedirect = false });
    }
    internal async Task<JsonElement> Bootstrap()
    {
        var body = await Send("GET", "/session", 200);
        Csrf = body.GetProperty("csrfToken").GetString();
        Family = body.GetProperty("familyId").ValueKind == JsonValueKind.String ? body.GetProperty("familyId").GetString() : null;
        return body;
    }
    internal async Task<JsonElement> Login(string username)
    {
        var result = await Send("POST", "/auth/login", 200, new { username, password = Password });
        Csrf = result.GetProperty("csrfToken").GetString();
        Family = result.GetProperty("familyId").GetString();
        return result;
    }
    internal async Task<JsonElement> Send(string method, string path, int status, object? body = null,
        bool csrf = true, string? origin = null, string? family = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/web-api" + path);
        request.Headers.Add("Origin", origin ?? Client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        if (csrf && Csrf is not null) request.Headers.Add("X-Zapara-CSRF", Csrf);
        if ((family ?? Family) is { } value) request.Headers.Add("X-Zapara-Family", value);
        if (body is not null) request.Content = JsonContent.Create(body, options: AccountJson.CreateOptions());
        using var response = await Client.SendAsync(request, Ct);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            foreach (var cookie in setCookies)
            {
                var pair = cookie.Split(';', 2)[0];
                cookies[pair.Split('=', 2)[0]] = pair;
            }
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(status == (int)response.StatusCode, $"{method} {path}: expected {status}, actual {(int)response.StatusCode}: {text}");
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
    internal async Task<JsonElement> Read(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    public async ValueTask DisposeAsync() { Client.Dispose(); await Factory.DisposeAsync(); }
}
