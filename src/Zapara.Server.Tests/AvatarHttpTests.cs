using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Social;
using Zapara.Server.Web;

namespace Zapara.Server.Tests;

public sealed class AvatarHttpTests
{
    private const string Token = "za_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaA";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid Owner = Guid.Parse("31817bf5-c9b5-4a31-a1a1-1c33d7cbd4bf");

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task All_native_routes_require_authentication(int version)
    {
        var service = new FakeAvatars();
        await using var app = await Host(service);
        using var client = app.GetTestClient();
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, $"users/{Owner:D}"), (HttpMethod.Put, "me"), (HttpMethod.Delete, "me"),
            (HttpMethod.Get, $"groups/{Owner:D}"), (HttpMethod.Put, $"groups/{Owner:D}"), (HttpMethod.Delete, $"groups/{Owner:D}")
        })
        {
            using var request = new HttpRequestMessage(method, $"/api/v{version}/social/avatars/{path}");
            using var response = await client.SendAsync(request, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Get_revalidates_access_before_etag_and_disposes_streams()
    {
        var service = new FakeAvatars();
        await using var app = await Host(service);
        using var client = Client(app);
        var path = $"/api/v1/social/avatars/users/{Owner:D}";
        using var first = await client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/webp", first.Content.Headers.ContentType?.MediaType);
        Assert.True(first.Headers.CacheControl?.Private);
        Assert.True(first.Headers.CacheControl?.NoCache);
        Assert.False(service.LastStream!.CanRead);
        client.DefaultRequestHeaders.IfNoneMatch.Add(first.Headers.ETag!);
        using var cached = await client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
        Assert.False(service.LastStream!.CanRead);
        Assert.Equal(2, service.Calls);
        service.Denied = true;
        using var denied = await client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.True(denied.Headers.CacheControl?.NoStore);
        Assert.Equal(3, service.Calls);
    }

    [Fact]
    public async Task Upload_and_delete_use_contract_and_group_target()
    {
        var service = new FakeAvatars();
        await using var app = await Host(service);
        using var client = Client(app);
        using var form = Form(new byte[20]);
        using var put = await client.PutAsync($"/api/v2/social/avatars/groups/{Owner:D}", form, Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        using var json = JsonDocument.Parse(await put.Content.ReadAsStringAsync(Ct));
        Assert.Equal(service.Revision, json.RootElement.GetProperty("revision").GetString());
        Assert.Equal(Owner, service.Group);
        Assert.Equal(20, service.Input!.Length);
        using var delete = await client.DeleteAsync("/api/v1/social/avatars/me", Ct);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Null(service.Group);
    }

    [Fact]
    public async Task Upload_rejects_extra_fields_wrong_field_and_oversize_before_service()
    {
        var service = new FakeAvatars();
        await using var app = await Host(service);
        using var client = Client(app);
        using (var wrong = Form(new byte[20], "avatar"))
        using (var result = await client.PutAsync("/api/v1/social/avatars/me", wrong, Ct))
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        using (var extra = Form(new byte[20]))
        {
            extra.Add(new StringContent("unexpected"), "other");
            using var result = await client.PutAsync("/api/v1/social/avatars/me", extra, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        }
        using (var large = Form(new byte[AvatarCompressor.MaxInputBytes + 1]))
        using (var result = await client.PutAsync("/api/v1/social/avatars/me", large, Ct))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, result.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Browser_routes_require_cookie_session_and_csrf_even_with_native_bearer()
    {
        var service = new FakeAvatars();
        await using var app = await Host(service);
        using var client = Client(app);
        using (var result = await client.GetAsync($"/web-api/social/avatars/users/{Owner:D}", Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        foreach (var target in new[] { "me", $"groups/{Owner:D}" })
        {
            using var form = Form(new byte[20]);
            using var put = await client.PutAsync("/web-api/social/avatars/" + target, form, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
            using var delete = await client.DeleteAsync("/web-api/social/avatars/" + target, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        }
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        using var crossSite = await client.GetAsync($"/web-api/social/avatars/groups/{Owner:D}", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, crossSite.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    private static MultipartFormDataContent Form(byte[] bytes, string name = "file")
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), name, "photo.png");
        return form;
    }

    private static HttpClient Client(WebApplication app)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    private static async Task<WebApplication> Host(FakeAvatars service)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration["Accounts:Enabled"] = "true";
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IAvatarService>(service);
        builder.Services.AddSingleton(new WebBrowserState(new EphemeralDataProtectionProvider(), TimeProvider.System));
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuth>("test", _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy("AccountUser", policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("account-other", rate => { rate.PermitLimit = 1000; rate.Window = TimeSpan.FromMinutes(1); }));
        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        var routes = typeof(Program).Assembly.GetType("Zapara.Server.Web.SocialHttp")!;
        routes.GetMethod("MapNative", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [app]);
        var browser = typeof(Program).Assembly.GetType("Zapara.Server.Web.WebEndpoints")!;
        browser.GetMethod("MapSocial", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [app.MapGroup("/web-api")]);
        await app.StartAsync(Ct);
        return app;
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization != "Bearer " + Token) return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Owner.ToString())], Scheme.Name)), Scheme.Name)));
        }
    }

    private sealed class FakeAvatars : IAvatarService
    {
        public string Revision { get; } = "4c927213-725c-41f5-a3e0-a7e491bf96d8";
        public int Calls { get; private set; }
        public bool Denied { get; set; }
        public MemoryStream? LastStream { get; private set; }
        public byte[]? Input { get; private set; }
        public Guid? Group { get; private set; }
        public Task<AvatarDownload> OpenUserAsync(string token, Guid userId, CancellationToken ct = default)
        {
            Calls++;
            if (Denied) throw new SocialException(404, "not_found");
            LastStream = new MemoryStream(new byte[20]);
            return Task.FromResult(new AvatarDownload(LastStream, Revision));
        }
        public Task<AvatarDownload> OpenGroupAsync(string token, Guid communityId, CancellationToken ct = default)
            => OpenUserAsync(token, communityId, ct);
        public Task<AvatarRevision> PutAsync(string token, Guid? communityId, byte[] input, CancellationToken ct = default)
        { Calls++; Group = communityId; Input = input; return Task.FromResult(new AvatarRevision(Revision)); }
        public Task DeleteAsync(string token, Guid? communityId, CancellationToken ct = default)
        { Calls++; Group = communityId; return Task.CompletedTask; }
    }
}
