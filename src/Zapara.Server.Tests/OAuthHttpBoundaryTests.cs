using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.OAuthTestHarness;

namespace Zapara.Server.Tests;

public sealed class OAuthHttpBoundaryTests
{
    [Fact]
    public async Task Callback_errors_are_plain_russian_nostore_and_do_not_echo_upstream()
    {
        await using var h = await Create();
        await using var host = new AccountApiTestHost(h.Db, clock: h.Clock);
        await using var factory = host.Factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        { s.RemoveAll<ExternalProviderRegistry>(); s.AddSingleton(h.Registry); s.RemoveAll<ExternalAuthService>(); s.AddSingleton(h.Service); }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://example.invalid"), AllowAutoRedirect = false });
        var p = await h.Start();
        using var response = await client.GetAsync(Callback + "?state=" + p.State + "&error=denied&error_description=upstream-canary", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.Matches("[А-Яа-я]", text);
        Assert.DoesNotContain("upstream-canary", text);
        Assert.Equal(0, h.Handler.Count);
        using var duplicate = await client.GetAsync(Callback + "?state=" + p.State + "&code=synthetic-code", Ct);
        Assert.Equal(HttpStatusCode.Gone, duplicate.StatusCode);
        Assert.Equal(0, h.Handler.Count);
    }

    [Fact]
    public async Task New_anonymous_routes_bound_bodies_and_rate_limits()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        await host.Send("POST", "/auth/external/exchange", 413, raw: new string('x', 16385));
        await host.Send("POST", "/auth/external/yandex/start", 413, raw: new string('x', 16385));
        for (var i = 0; i < 9; i++)
            await host.Send("POST", "/auth/external/yandex/start", 503, new { }, code: "provider_unavailable");
        await host.Send("POST", "/auth/external/yandex/start", 429, new { }, code: "rate_limited");
    }

    [Theory]
    [InlineData("https://attacker.invalid", 45001)]
    [InlineData("windows", 80)]
    [InlineData("windows", 65536)]
    [InlineData("android", 45001)]
    public async Task Native_return_descriptors_cannot_be_arbitrary_urls_or_ports(string kind, int port)
    {
        await using var h = await Create();
        await Assert.ThrowsAsync<ExternalAuthException>(() => h.Service.StartAsync("yandex", new("login", ExternalSecrets.Random(), "S256",
            new(Guid.NewGuid(), "Тест", "windows"), new(kind, port)), ct: Ct));
        Assert.Equal(0, h.Handler.Count);
    }

    [Fact]
    public async Task Provider_callback_registry_consistency_rejects_different_configured_path()
    {
        using var adapter = new Zapara.Server.Accounts.ExternalProviders.YandexIdAdapter(new("synthetic-client", Callback));
        using var registry = new ExternalProviderRegistry([adapter], new Dictionary<string, string> { ["yandex"] = "https://example.invalid/wrong-path" });
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new ExternalAuthService(db.DataSource, db.Configuration, TimeProvider.System, registry);
        await Assert.ThrowsAsync<ExternalAuthException>(() => service.StartAsync("yandex", new("login", ExternalSecrets.Random(), "S256",
            new(Guid.NewGuid(), "Тест", "windows"), new("windows", 45001)), ct: Ct));
    }
}
