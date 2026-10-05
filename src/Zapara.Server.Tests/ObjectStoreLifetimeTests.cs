using System.Net;
using Microsoft.Extensions.Configuration;
using Xunit;
using Zapara.Server.Storage;

namespace Zapara.Server.Tests;

public sealed class ObjectStoreLifetimeTests
{
    [Fact]
    public void Router_reuses_transport_across_requests_and_releases_it_at_shutdown()
    {
        var handler = new StorageHandler(HttpStatusCode.OK);
        var created = 0;
        var router = new RoutingObjectStore(Configuration(), _ => { created++; return new HttpClient(handler); });
        router.Put("notes.bin", [1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, router.Get("notes.bin"));
        router.Delete("notes.bin");
        Assert.Equal(1, created);
        Assert.False(handler.Disposed);
        Assert.IsAssignableFrom<IDisposable>(router).Dispose();
        Assert.True(handler.Disposed);
    }

    [Fact]
    public void Store_does_not_dispose_a_borrowed_http_client()
    {
        var handler = new StorageHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var store = new S3ObjectStore(Target, http);
        store.Put("notes.bin", [1, 2, 3]);
        Assert.IsAssignableFrom<IDisposable>(store).Dispose();
        Assert.False(handler.Disposed);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://storage.example/health");
        using var response = http.Send(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Unavailable_remote_status_reads_existing_local_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), "zapara-store-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "notes.bin"), [7, 8, 9]);
            using var handler = new StorageHandler(HttpStatusCode.ServiceUnavailable);
            var router = new RoutingObjectStore(Configuration(root), _ => new HttpClient(handler, disposeHandler: false));
            try { Assert.Equal(new byte[] { 7, 8, 9 }, router.Get("notes.bin")); }
            finally { ((object)router as IDisposable)?.Dispose(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static readonly S3Target Target = new("https://storage.example", "ru-central1", "bucket", "synthetic-key", "synthetic-secret");

    private static IConfiguration Configuration(string? root = null) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Social:MediaRoot"] = root ?? Path.Combine(Path.GetTempPath(), "zapara-store-unused"),
        ["S3:Endpoint"] = Target.Endpoint, ["S3:Region"] = Target.Region, ["S3:Bucket"] = Target.Bucket,
        ["S3:AccessKey"] = Target.AccessKey, ["S3:Secret"] = Target.Secret
    }).Build();

    private sealed class StorageHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public bool Disposed { get; private set; }
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => new(status) { Content = new ByteArrayContent([1, 2, 3]) };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
