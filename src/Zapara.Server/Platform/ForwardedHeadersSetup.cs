using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.HttpOverrides;
using AspNetIPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace Zapara.Server.Platform;

/// <summary>
/// Which peers may set X-Forwarded-For / X-Forwarded-Proto. Only the reverse proxy (Caddy on the Docker network)
/// should be trusted, otherwise any client that reaches Kestrel directly could choose its own address and with it
/// its rate-limit and login-throttle keys.
/// Configuration: <c>ForwardedHeaders:KnownNetworks</c> (CIDRs) and <c>ForwardedHeaders:KnownProxies</c> (addresses),
/// each either a comma/semicolon separated string or a list. When neither is set, loopback and private ranges
/// (where Docker bridge networks live) are trusted.
/// </summary>
public static class ForwardedHeadersSetup
{
    public const string Section = "ForwardedHeaders";

    public static readonly IReadOnlyList<string> DefaultNetworks =
        ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"];

    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        // TLS ends at the reverse proxy; one proxy hop in front of Kestrel.
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        var section = configuration.GetSection(Section);
        var networks = Values(section.GetSection("KnownNetworks"));
        var proxies = Values(section.GetSection("KnownProxies"));
        if (networks.Count == 0 && proxies.Count == 0) networks = [.. DefaultNetworks];
        foreach (var network in networks) options.KnownNetworks.Add(ParseNetwork(network));
        foreach (var proxy in proxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
                throw new InvalidOperationException($"{Section}:KnownProxies: invalid address.");
            options.KnownProxies.Add(address);
        }
    }

    public static AspNetIPNetwork ParseNetwork(string value)
    {
        var parts = value.Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var prefix))
            throw new InvalidOperationException($"{Section}:KnownNetworks: invalid network.");
        var max = prefix.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var length = max;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out length) || length < 0 || length > max))
            throw new InvalidOperationException($"{Section}:KnownNetworks: invalid prefix length.");
        return new AspNetIPNetwork(prefix, length);
    }

    private static List<string> Values(IConfigurationSection section)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(section.Value))
            values.AddRange(section.Value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (var child in section.GetChildren())
            if (!string.IsNullOrWhiteSpace(child.Value)) values.Add(child.Value.Trim());
        return values;
    }
}
