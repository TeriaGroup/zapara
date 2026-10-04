using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zapara.Server.Operator;
using Zapara.Server.Social;

namespace Zapara.Server.Tests;

public sealed class GenericFileAccessTests
{
    [Theory]
    [InlineData("/api/v1/files/", "supf00112233445566778899aabbccddeeff")]
    [InlineData("/web-api/files/", "text00112233445566778899aabbccddeeff")]
    [InlineData("/api/v1/homework-files/", "gfile00112233445566778899aabbccddeeff")]
    [InlineData("/web-api/homework-files/", "avatar-00112233445566778899aabbccddeeff.webp")]
    public async Task Generic_download_routes_cannot_read_private_object_names(string route, string name)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Accounts:Enabled"] = "true";
        var objects = new MemoryObjectStore();
        objects.Put(name, [7, 8, 9]);
        builder.Services.AddSingleton<IObjectStore>(objects);
        await using var app = builder.Build();
        app.MapSupport();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(route + name, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }
}
