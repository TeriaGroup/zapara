using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Zapara.Server.Tests;

public sealed class ForwardedPeerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Forwarded_address_and_https_require_an_actual_transport_peer(bool peer)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("Accounts:Enabled", "false"));
        using var client = factory.CreateClient();
        var result = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = peer ? IPAddress.Loopback : null;
            context.Request.Method = "GET";
            context.Request.Scheme = "http";
            context.Request.Path = "/health/live";
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        }, TestContext.Current.CancellationToken);
        Assert.Equal(200, result.Response.StatusCode);
        Assert.Equal(peer ? "https" : "http", result.Request.Scheme);
        Assert.Equal(peer ? IPAddress.Parse("203.0.113.7") : null, result.Connection.RemoteIpAddress);
    }
}
