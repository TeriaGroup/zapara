using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Server.Social;

namespace Zapara.Server.Tests;

[Collection("Account runtime")]
public sealed class SupportBrowserSessionTests
{
    [Fact]
    public async Task Authenticated_support_requires_csrf_and_valid_requests_remain_uncacheable()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, initialize: true);
        await using var host = new WebAccountHost(db, moduleSettings: new() { ["Operator:Schema"] = db.Schema });
        var schema = host.Factory.Services.GetRequiredService<SocialConfiguration>().QuotedSchema;
        try
        {
            await host.Bootstrap();
            await host.Send("POST", "/auth/register", 201, new RegisterRequest("support.browser", WebAccountHost.Password));
            await host.Login("support.browser");
            var body = new { subject = "Synthetic report", body = "Synthetic support message" };
            await host.Send("POST", "/support", 403, body, csrf: false);
            await host.Send("POST", "/support", 200, body);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/web-api/support");
            request.Headers.Add("Origin", "https://localhost");
            request.Headers.Add("X-Zapara-Family", host.Family);
            using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Contains("Synthetic support message", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await host.DisposeAsync();
            await db.ExecuteAsync($"DROP SCHEMA IF EXISTS {schema} CASCADE");
        }
    }
}
