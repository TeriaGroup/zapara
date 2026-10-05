using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;
using static Zapara.Server.Tests.ApiTestFactory;

namespace Zapara.Server.Tests;

public sealed class PlatformReadyHttpTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Timetable_ready_stays_200_on_fixture_host_and_platform_lists_modules_without_secrets()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await PublishAsync(db);
        await using var factory = Create(db.Configuration, db.Clock);
        using var client = factory.CreateClient();

        var ready = await GetAsync(client, "/health/ready");
        Assert.Equal("ready", ready.GetProperty("status").GetString());
        Keys(ready, "status");

        var platform = await GetAsync(client, "/health/platform");
        Assert.Equal("ready", platform.GetProperty("status").GetString());
        Keys(platform, "status", "modules");
        var modules = platform.GetProperty("modules");
        Assert.Equal("present", modules.GetProperty("timetable").GetString());
        Assert.Equal("disabled", modules.GetProperty("accounts").GetString());
        Assert.Equal("disabled", modules.GetProperty("sync").GetString());
        Assert.Equal("disabled", modules.GetProperty("communities").GetString());
        Assert.Equal("disabled", modules.GetProperty("admin").GetString());
        var text = platform.GetRawText();
        Assert.DoesNotContain("Password=", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET_canary", text, StringComparison.Ordinal);
        var dsn = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES");
        if (!string.IsNullOrEmpty(dsn))
            Assert.DoesNotContain(dsn, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_host_keeps_timetable_ready_contract_and_platform_returns_modules_without_secrets()
    {
        const string canary = "Host=evil;Password=SECRET_canary_dsn_value";
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Timetable"] = null,
                    ["Timetable:Schema"] = null,
                    ["ConnectionStrings:Accounts"] = canary
                })));
        using var client = factory.CreateClient();
        await ProblemAsync(client, "/health/ready", HttpStatusCode.ServiceUnavailable, "db_unavailable");

        using var response = await client.GetAsync("/health/platform", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync(Ct);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        Assert.Equal("platform_not_ready", root.GetProperty("code").GetString());
        var modules = root.GetProperty("modules");
        Assert.Equal("missing", modules.GetProperty("timetable").GetString());
        Assert.Equal("disabled", modules.GetProperty("accounts").GetString());
        Assert.Equal("disabled", modules.GetProperty("sync").GetString());
        Assert.Equal("disabled", modules.GetProperty("communities").GetString());
        Assert.Equal("disabled", modules.GetProperty("admin").GetString());
        Assert.DoesNotContain(canary, text, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_canary", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", text, StringComparison.OrdinalIgnoreCase);
    }
}
