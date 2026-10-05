using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Zapara.Server.Tests;

public sealed class PublicCatalogTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static WebApplicationFactory<Program> Host() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
        .UseEnvironment("Testing").ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "false", ["Sync:Enabled"] = "false", ["Communities:Enabled"] = "false",
            ["Admin:Enabled"] = "false", ["Web:Enabled"] = "false", ["Timetable:Refresh:Enabled"] = "false"
        })));

    [Fact]
    public async Task Teacher_catalog_and_own_timetable_share_version_and_allow_conditional_refresh()
    {
        await using var host = Host();
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/api/v1/teachers", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var first = json.RootElement.GetProperty("lecturers")[0].GetProperty("id").GetString();
        var version = json.RootElement.GetProperty("version").GetString();
        Assert.Equal(64, version!.Length);
        using var detailResponse = await client.GetAsync("/api/v1/teachers/" + Uri.EscapeDataString(first!) + "/timetable", Ct);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync(Ct));
        Assert.Equal(version, detail.RootElement.GetProperty("version").GetString());
        Assert.Equal(first, detail.RootElement.GetProperty("lecturer").GetProperty("id").GetString());
        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/teachers");
        conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        using var unchanged = await client.SendAsync(conditional, Ct);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        using var missing = await client.GetAsync("/api/v1/teachers/no-such-lecturer/timetable", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task All_nine_maps_graph_and_coordinates_match_manifest_hashes_and_allowlist()
    {
        await using var host = Host();
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/api/v1/maps/manifest", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("graphVersion").GetInt32());
        Assert.Equal(9, root.GetProperty("maps").GetArrayLength());
        var assets = root.GetProperty("maps").EnumerateArray().Concat([root.GetProperty("graph"), root.GetProperty("coordinates")]);
        foreach (var asset in assets)
        {
            using var file = await client.GetAsync(asset.GetProperty("url").GetString(), Ct);
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
            var bytes = await file.Content.ReadAsByteArrayAsync(Ct);
            Assert.Equal(asset.GetProperty("bytes").GetInt32(), bytes.Length);
            Assert.Equal(asset.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            Assert.Equal("nosniff", file.Headers.GetValues("X-Content-Type-Options").Single());
        }
        using var denied = await client.GetAsync("/api/v1/maps/assets/appsettings.json", Ct);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }
}
