using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.AccountApiTestHost;

namespace Zapara.Server.Tests;

public sealed partial class AccountApiTests
{
    [Fact]
    public async Task ACC03_Concurrent_refresh_commits_replay_revocation()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        await host.Register(); var session = await host.Login();
        async Task<HttpResponseMessage> Rotate() => await host.Client.PostAsync("/api/v1/auth/refresh",
            new StringContent(JsonSerializer.Serialize(new { refreshToken = session.RefreshToken }), Encoding.UTF8, "application/json"), Ct);
        var responses = await Task.WhenAll(Rotate(), Rotate());
        try
        {
            Assert.Equal(new[] { 200, 401 }, responses.Select(r => (int)r.StatusCode).Order());
            var winner = JsonSerializer.Deserialize<SessionResponse>(await responses.Single(r => r.IsSuccessStatusCode).Content.ReadAsStringAsync(Ct), Json)!;
            await host.InvalidAccess(winner.AccessToken);
            await host.Send("POST", "/auth/refresh", 401, new { refreshToken = winner.RefreshToken }, code: "invalid_session");
            Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.session_families WHERE revoked_at IS NOT NULL"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task ACC02_Expiry_boundaries_and_unknown_refresh_without_collateral_revocation()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        var clock = new StoreTestTimeProvider();
        await using var host = new AccountApiTestHost(db, clock: clock);
        await host.Register(); var session = await host.Login();
        await host.Send("POST", "/auth/refresh", 401, new { refreshToken = "zr_" + new string('A', 43) }, code: "invalid_session");
        await host.Send("GET", "/account/me", 200, bearer: session.AccessToken);
        clock.Now = session.AccessExpiresAt;
        await host.InvalidAccess(session.AccessToken);
        var next = await host.Refresh(session.RefreshToken);
        clock.Now = next.RefreshExpiresAt;
        await host.InvalidAccess(next.AccessToken);
        await host.Send("POST", "/auth/refresh", 401, new { refreshToken = next.RefreshToken }, code: "invalid_session");
    }

    [Fact]
    public async Task Strict_protected_bodies_UUID_queries_and_empty_actions()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        await host.Register(); var session = await host.Login();
        foreach (var raw in new[] { "{}", "{\"displayName\":null,\"userId\":\"foreign\"}",
            "{\"username\":\"changed\"}", "{\"displayName\":null,\"display\\u004eame\":\"duplicate\"}", "{\"displayName\":12}" })
            await host.Send("PATCH", "/account/me", 400, bearer: session.AccessToken, raw: raw, code: "invalid_request");
        foreach (var id in new[] { "bad", Guid.Empty.ToString(), Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString().ToUpperInvariant() })
        {
            await host.Send("DELETE", "/account/devices/" + id, 400, bearer: session.AccessToken, code: "invalid_request");
            await host.Send("DELETE", "/account/devices/" + id, 401, code: "invalid_session");
        }
        foreach (var query in new[] { "limit=0", "limit=101", "limit=-1", "limit=one", "limit=1&limit=2", "cursor=", "cursor=bad", "userId=foreign" })
            await host.Send("GET", "/account/devices?" + query, 400, bearer: session.AccessToken, code: "invalid_request");
        foreach (var path in new[] { "/auth/logout", "/account/sessions/revoke-all", "/account/devices/" + session.FamilyId })
        {
            var method = path.Contains("devices") ? "DELETE" : "POST";
            foreach (var raw in new[] { "{}", "{\"extra\":1}", "null", "x" })
                await host.Send(method, path, 400, bearer: session.AccessToken, raw: raw, code: "invalid_request");
            await host.Send(method, path, 413, bearer: session.AccessToken, raw: new string(' ', 16385), code: "invalid_request");
        }
        await host.Send("POST", "/auth/logout", 204, bearer: session.AccessToken, raw: new string(' ', 16384));
    }

    [Fact]
    public async Task Authentication_principal_is_service_derived_forbid_is_safe_and_headers_are_strict()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        await host.Register(); var session = await host.Login();
        using var scope = host.Factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Response.Body = new MemoryStream();
        context.Request.Headers.Authorization = "bEaReR " + session.AccessToken;
        var authenticated = await context.AuthenticateAsync(OpaqueAccountHandler.SchemeName);
        Assert.True(authenticated.Succeeded);
        Assert.Equal(session.User.UserId.ToString(), authenticated.Principal!.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        Assert.Equal(session.FamilyId.ToString(), authenticated.Principal.FindFirst("family_id")!.Value);
        Assert.True(!string.Join(" ", authenticated.Principal.Claims.Select(c => c.Value)).Contains(session.AccessToken), "Principal must contain no bearer.");
        await context.ForbidAsync(OpaqueAccountHandler.SchemeName);
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        context.Response.Body.Position = 0;
        Assert.Contains("forbidden", await new StreamReader(context.Response.Body).ReadToEndAsync(Ct));
        foreach (var headers in new[] { new[] { "Bearer " + session.AccessToken, "Bearer " + session.AccessToken },
            new[] { "Bearer " + new string('A', 8192) }, new[] { "Bearer\t" + session.AccessToken } })
        {
            // HttpClient normalizes a tab to a space before transport; submit raw server headers.
            var response = await host.Factory.Server.SendAsync(raw =>
            {
                raw.Request.Method = "GET";
                raw.Request.Path = "/api/v1/account/me";
                raw.Request.Headers.Authorization = headers;
            }, Ct);
            Assert.Equal(401, response.Response.StatusCode);
        }
        using var queryRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/account/me?access_token=" + session.AccessToken);
        queryRequest.Headers.TryAddWithoutValidation("Cookie", "access_token=" + session.AccessToken);
        using var queryResponse = await host.Client.SendAsync(queryRequest, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, queryResponse.StatusCode);
        Assert.True(!string.Join('\n', host.Logs).Contains(session.AccessToken), "Query credential must not be logged.");
    }

    [Fact]
    public async Task Strict_login_nested_duplicates_and_register_required_fields()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        foreach (var raw in new[] { "{}", "{\"username\":\"synthetic\"}", "{\"username\":\"synthetic\",\"password\":null}",
            "{\"username\":\"synthetic\",\"password\":\"short\"}", "{\"username\":\"synthetic\",\"password\":\"long enough canary\",\"extra\":1}" })
            await host.Send("POST", "/auth/register", 400, raw: raw, code: "invalid_request");
        var rawLogin = JsonSerializer.Serialize(LoginBody(), Json);
        foreach (var raw in new[] { rawLogin.Replace("\"platform\":\"windows\"", "\"platform\":\"windows\",\"platform\":\"android\""),
            rawLogin.Replace("\"platform\":\"windows\"", "\"platform\":\"windows\",\"actorId\":\"foreign\""),
            rawLogin.Replace("\"password\"", "\"Password\"") })
            await host.Send("POST", "/auth/login", 400, raw: raw, code: "invalid_request");
    }

    [Fact]
    public async Task Streamed_unknown_length_body_obeys_exact_byte_bound()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        foreach (var length in new[] { 16384, 16385 })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
            { Content = new UnknownLengthContent(Encoding.UTF8.GetBytes(new string(' ', length))) };
            using var response = await host.Client.SendAsync(request, Ct);
            Assert.Equal(length == 16384 ? 400 : 413, (int)response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] bytes;
        public UnknownLengthContent(byte[] bytes)
        {
            this.bytes = bytes;
            Headers.ContentType = new("application/json");
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes, Ct).AsTask();
    }
}
