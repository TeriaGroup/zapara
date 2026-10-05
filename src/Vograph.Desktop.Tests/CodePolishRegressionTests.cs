using System.Net;
using System.Text;
using System.Xml;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class CodePolishRegressionTests
{
    [Fact]
    public async Task Malformed_lecturer_download_preserves_last_good_cache()
    {
        using var db = TestDb.Create();
        var cache = Path.Combine(db.Dir, "teachers-last-good.xml");
        var good = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-lecturers.xml"));
        await File.WriteAllTextAsync(cache, good, TestContext.Current.CancellationToken);
        using var client = new HttpClient(new Reply(HttpStatusCode.OK, new TrackedContent("<Timetable><Lecturer>")));
        var service = new LecturerService(cachePath: cache, bundledPath: Path.Combine(db.Dir, "missing.xml"));

        var result = await service.FetchXmlAsync("https://example.invalid/lecturers", client);

        Assert.True(result.fromCache);
        Assert.Equal(good, result.xml);
        Assert.Equal(good, await File.ReadAllTextAsync(cache, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Malformed_lecturer_download_is_not_persisted_without_a_cache()
    {
        using var db = TestDb.Create();
        var cache = Path.Combine(db.Dir, "teachers-missing.xml");
        using var client = new HttpClient(new Reply(HttpStatusCode.OK, new TrackedContent("<Timetable><Lecturer>")));
        var service = new LecturerService(cachePath: cache, bundledPath: Path.Combine(db.Dir, "missing.xml"));

        await Assert.ThrowsAsync<XmlException>(() => service.FetchXmlAsync("https://example.invalid/lecturers", client));

        Assert.False(File.Exists(cache));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Release_metadata_response_is_disposed_on_success_and_failure(HttpStatusCode status)
    {
        var content = new TrackedContent("[]");
        using var client = new HttpClient(new Reply(status, content));
        var service = new AutoUpdateService(client);

        if (status == HttpStatusCode.OK)
            Assert.Null(await service.GetLatestAsync(ct: TestContext.Current.CancellationToken));
        else
            await Assert.ThrowsAsync<HttpRequestException>(() => service.GetLatestAsync(ct: TestContext.Current.CancellationToken));

        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task Disposing_update_service_preserves_borrowed_client_ownership()
    {
        var handler = new Reply(HttpStatusCode.OK, new TrackedContent("[]"));
        using var client = new HttpClient(handler);
        var service = new AutoUpdateService(client);

        Assert.IsAssignableFrom<IDisposable>(service).Dispose();

        Assert.False(handler.Disposed);
        using var response = await client.GetAsync("https://example.invalid/borrowed", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class Reply(HttpStatusCode status, HttpContent content) : HttpMessageHandler
    {
        public bool Disposed { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = content });
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class TrackedContent(string body) : StringContent(body, Encoding.UTF8, "application/json")
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
