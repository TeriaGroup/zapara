using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Xunit;
using Zapara.Server.Platform;

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

    [Theory]
    [InlineData(null, "127.0.0.1", true)]
    [InlineData(null, "172.18.0.3", true)]
    [InlineData(null, "::ffff:172.18.0.3", true)]
    [InlineData(null, "198.51.100.5", false)]
    [InlineData("172.18.0.0/16", "172.18.0.3", true)]
    [InlineData("172.18.0.0/16", "127.0.0.1", false)]
    [InlineData("172.18.0.0/16", "10.0.0.2", false)]
    [InlineData("172.18.0.0/16;10.9.0.0/24", "10.9.0.7", true)]
    public async Task Forwarded_headers_are_trusted_only_from_configured_proxy_networks(string? networks, string peer, bool trusted)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Accounts:Enabled", "false");
            if (networks is not null) builder.UseSetting("ForwardedHeaders:KnownNetworks", networks);
        });
        using var client = factory.CreateClient();
        var result = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Method = "GET";
            context.Request.Scheme = "http";
            context.Request.Path = "/health/live";
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        }, TestContext.Current.CancellationToken);
        Assert.Equal(200, result.Response.StatusCode);
        Assert.Equal(trusted ? "https" : "http", result.Request.Scheme);
        Assert.Equal(trusted ? IPAddress.Parse("203.0.113.7") : IPAddress.Parse(peer), result.Connection.RemoteIpAddress);
    }

    [Fact]
    public void Known_proxies_and_lists_are_read_and_invalid_values_fail_startup()
    {
        var options = new ForwardedHeadersOptions();
        ForwardedHeadersSetup.Configure(options, Config(("ForwardedHeaders:KnownProxies:0", "172.18.0.2"), ("ForwardedHeaders:KnownProxies:1", "fd00::2")));
        Assert.Empty(options.KnownNetworks);
        Assert.Equal([IPAddress.Parse("172.18.0.2"), IPAddress.Parse("fd00::2")], options.KnownProxies);
        options = new ForwardedHeadersOptions();
        ForwardedHeadersSetup.Configure(options, Config());
        Assert.Equal(ForwardedHeadersSetup.DefaultNetworks.Count, options.KnownNetworks.Count);
        Assert.Empty(options.KnownProxies);
        foreach (var bad in new[] { "172.18.0.0/33", "not-an-ip/8", "::/129", "10.0.0.0/x" })
            Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.Configure(new ForwardedHeadersOptions(), Config(("ForwardedHeaders:KnownNetworks", bad))));
        Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.Configure(new ForwardedHeadersOptions(), Config(("ForwardedHeaders:KnownProxies", "proxy.local"))));
    }

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();
}
