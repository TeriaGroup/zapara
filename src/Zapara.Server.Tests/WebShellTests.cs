using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Zapara.Server.Tests;

public sealed class WebShellTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WebApplicationFactory<Program> Host(bool enabled = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Web:Enabled"] = enabled.ToString(),
                    ["Accounts:Enabled"] = "false",
                    ["Sync:Enabled"] = "false",
                    ["Communities:Enabled"] = "false",
                    ["Admin:Enabled"] = "false"
                })));

    [Theory]
    [InlineData("/app/")]
    [InlineData("/app/schedule")]
    [InlineData("/app/maps")]
    public async Task Browser_routes_load_the_Russian_shell_without_a_database(string route)
    {
        await using var host = Host();
        using var client = host.CreateClient();
        using var response = await client.GetAsync(route, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("lang=\"ru\"", html, StringComparison.Ordinal);
        Assert.Contains("<base href=\"/app/\"", html, StringComparison.Ordinal);
        Assert.Contains("manifest.webmanifest", html, StringComparison.Ordinal);
        Assert.Contains("id=\"root\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.webassembly.js", html, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Theory]
    [InlineData("/api/v1/not-a-route")]
    [InlineData("/web-api/not-a-route")]
    [InlineData("/Admin/not-a-route")]
    [InlineData("/app/missing.js")]
    public async Task Shell_fallback_does_not_mask_missing_APIs_admin_or_assets(string route)
    {
        await using var host = Host();
        using var client = host.CreateClient();
        using var response = await client.GetAsync(route, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Disabled_web_does_not_publish_the_shell()
    {
        await using var host = Host(false);
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/app/", Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/app/fonts/inter_regular.ttf", 100000)]
    [InlineData("/app/data/TimetableGroup50.xml", 100000)]
    [InlineData("/app/maps/karta-glavnyj-korpus-1-etazh-2022.jpg", 10000)]
    public async Task Offline_resources_are_reachable_from_the_shell_base_path(string route, int minimumBytes)
    {
        await using var host = Host();
        using var client = host.CreateClient();
        using var response = await client.GetAsync(route, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadAsByteArrayAsync(Ct)).Length > minimumBytes);
    }
}
