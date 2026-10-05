using System.Net;

namespace Zapara.Server.Accounts.ExternalProviders;

public static class ProviderHttpClientFactory
{
    // Direct BCL handler: no IHttpClientFactory logging handler, redirects, cookies or decompression.
    // Callers own clients created here. Adapters own only their internally created default client.
    public static HttpClient Create() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(30),
        MaxResponseHeadersLength = 16,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
}
