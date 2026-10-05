using System.Diagnostics;
using System.Net;
using Vograph.Core.Models;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class TeacherCatalogPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Real_packaged_catalog_cold_load_and_repeated_group_search_preserve_results()
    {
        var xml = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TimetableLecturer50.xml"), TestContext.Current.CancellationToken);
        using var http = new HttpClient(new Input(xml)) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser());
        var store = new BrowserLecturerStore(http, storage);
        var watch = Stopwatch.StartNew();
        await store.InitializeAsync();
        output.WriteLine($"Packaged initialization {watch.ElapsedMilliseconds}ms; lecturers={store.Lecturers.Count}");
        Assert.True(store.Lecturers.Count > 500);
        var teacher = store.Lecturers[0];
        var mine = store.Lecturers.Take(28).Select(value => new Lesson { TeacherRaw = value.Name }).ToArray();
        watch.Restart();
        for (var i = 0; i < 30; i++) Assert.Contains(store.Search("", true, mine), value => value.Id == teacher.Id);
        output.WriteLine($"Thirty real group filters {watch.ElapsedMilliseconds}ms");
    }
    private sealed class Input(string xml) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith(".xml") ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) } : new(HttpStatusCode.NotFound));
    }
}
