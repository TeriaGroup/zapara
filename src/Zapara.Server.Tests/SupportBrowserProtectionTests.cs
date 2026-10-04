using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Zapara.Server.Tests;

public sealed class SupportBrowserProtectionTests
{
    [Theory]
    [InlineData("POST", "/web-api/support")]
    [InlineData("POST", "/web-api/support/00112233-4455-6677-8899-aabbccddeeff")]
    [InlineData("POST", "/web-api/files")]
    [InlineData("POST", "/web-api/homework-files")]
    [InlineData("GET", "/web-api/support")]
    public async Task Cross_origin_support_requests_are_rejected_before_session_or_database_access(string method, string path)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Accounts:Enabled", "true");
            builder.UseSetting("Web:Enabled", "true");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = "support_test",
                ["ConnectionStrings:Accounts"] = "Host=127.0.0.1;Port=9;Database=unavailable",
                ["Web:Enabled"] = "true"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            });
        });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("Origin", "https://other.invalid");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("csrf_invalid", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
