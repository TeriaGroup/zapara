using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;
using static Zapara.Server.Tests.AccountApiTestHost;

namespace Zapara.Server.Tests;

public sealed class AccountAuthenticationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic abc")]
    [InlineData("Bearer malformed")]
    [InlineData("Bearer za_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")]
    [InlineData("Bearer za_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]
    public async Task ACC02_Invalid_header_safe_challenge(string? header)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/account/me?accessToken=canary");
        if (header is not null) request.Headers.TryAddWithoutValidation("Authorization", header);
        request.Headers.TryAddWithoutValidation("Cookie", "accessToken=canary");
        using var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("invalid_session", await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("bad-schema", "ignored")]
    [InlineData("acc_unused", "Password=dsn-canary;NotAKey=bad")]
    public async Task ACC02_Lazy_configuration_error_does_not_break_anonymous(string schema, string dsn)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine);
        await using var host = new AccountApiTestHost(db, overrides: new()
        { ["Accounts:Schema"] = schema, ["ConnectionStrings:Accounts"] = dsn });
        await host.Send("GET", "/account/me", 503, bearer: "za_" + new string('A', 43), code: "db_unavailable");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer za_" + new string('A', 43));
        using var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("dsn-canary", string.Join('\n', host.Logs));
    }

    [Fact]
    public async Task ACC02_Actual_database_outage_is_503_not_401()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var timetable = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await ApiTestFactory.PublishAsync(timetable);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        await using var host = new AccountApiTestHost(db, overrides: new()
        { ["ConnectionStrings:Accounts"] = $"Host=127.0.0.1;Port={port};Database=zapara_test;Username=synthetic;Password=outage-canary;Timeout=1" }, timetable: timetable);
        await host.Send("GET", "/account/me", 503, bearer: "za_" + new string('A', 43), code: "db_unavailable");
        await host.Send("POST", "/auth/login", 503, LoginBody(), code: "db_unavailable");
        host.Client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer za_" + new string('A', 43));
        await ApiTestFactory.GetAsync(host.Client, "/api/v1/groups");
        using var live = await host.Client.GetAsync("/health/live", Ct);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.DoesNotContain("outage-canary", string.Join('\n', host.Logs));
    }

    [Fact]
    public async Task Disabled_routes_absent_and_Production_registration_unavailable()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using (var disabled = new AccountApiTestHost(db, overrides: new() { ["Accounts:Enabled"] = "false" }))
        {
            using var response = await disabled.Client.GetAsync("/api/v1/account/me", Ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        await using (var local = new AccountApiTestHost(db)) await local.Register();
        await using var production = new AccountApiTestHost(db, "Production");
        await production.Send("POST", "/auth/register", 503, raw: "{}", code: "registration_unavailable");
        await production.Login();
    }

    [Fact]
    public async Task ACC10_Strict_streamed_body_and_refresh_shape()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        foreach (var raw in new[] { "{}", "null", "[]", "{", "{\"refreshToken\":null}", "{\"refreshToken\":3}",
                     "{\"refreshToken\":\"x\",\"extra\":1}", "{\"refreshToken\":\"x\",\"refreshToken\":\"y\"}",
                     "{\"RefreshToken\":\"x\"}" })
            await host.Send("POST", "/auth/refresh", 400, raw: raw, code: "invalid_request");
        foreach (var token in new[] { "", "x", "zr_" + new string('A', 43), "zr_" + new string('A', 42) + "B" })
            await host.Send("POST", "/auth/refresh", 401, new { refreshToken = token }, code: "invalid_session");
        await host.Send("POST", "/auth/refresh", 413, raw: new string(' ', 16385), code: "invalid_request");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        { Content = new ByteArrayContent(new byte[] { 0x7b, 0x22, 0xff, 0x22, 0x3a, 0x31, 0x7d }) };
        request.Content.Headers.ContentType = new("application/json");
        using var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var wrongMedia = await host.Client.PostAsync("/api/v1/auth/refresh", new StringContent("{}", Encoding.UTF8, "text/plain"), Ct);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongMedia.StatusCode);
    }

    [Theory]
    [InlineData("/auth/register", 5)]
    [InlineData("/auth/login", 10)]
    [InlineData("/auth/refresh", 60)]
    [InlineData("/account/me", 120)]
    public async Task Rate_limits_are_bounded_and_run_before_work(string path, int count)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine);
        await using var host = new AccountApiTestHost(db);
        for (var i = 0; i <= count; i++)
        {
            using var request = new HttpRequestMessage(path == "/account/me" ? HttpMethod.Get : HttpMethod.Post, "/api/v1" + path);
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", "192.0.2." + i);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await host.Client.SendAsync(request, Ct);
            Assert.Equal(i == count ? 429 : path == "/account/me" ? 401 : 400, (int)response.StatusCode);
            if (i == count)
            {
                Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
                Assert.True(response.Headers.CacheControl?.NoStore);
                Assert.Contains("rate_limited", await response.Content.ReadAsStringAsync(Ct));
            }
        }
        Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_class WHERE relnamespace='{db.Schema}'::regnamespace"));
    }
}
