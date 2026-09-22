using System.Net;
using System.Net.Http.Json;
using Xunit;
using Zapara.Web.Services;

namespace Zapara.Web.Tests;

public sealed class TeacherFreshnessTests
{
    [Fact]
    public async Task Server_packaged_fallback_is_labelled_and_retained_after_offline_restart()
    {
        var disk = new MemoryBrowser();
        using var http = new HttpClient(new Handler()) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(disk);
        var store = new BrowserLecturerStore(http, storage);
        await store.InitializeAsync();
        Assert.Contains("встроенн", store.FreshnessNotice, StringComparison.OrdinalIgnoreCase);
        var restarted = new BrowserLecturerStore(new HttpClient(new Handler { Offline = true }) { BaseAddress = http.BaseAddress }, storage);
        await restarted.InitializeAsync();
        Assert.Single(restarted.Lecturers);
        Assert.Contains("встроенн", restarted.FreshnessNotice, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public bool Offline { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline) throw new HttpRequestException();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                version = "v1", lecturers = new[] { new { id = "one", name = "Преподаватель", kafedra = "О6" } },
                metadata = new { source = "packaged", fetchedAt = (DateTimeOffset?)null, lastAttemptAt = (DateTimeOffset?)null, lastFailure = (string?)null, cacheLifetime = "memory" }
            }) });
        }
    }
}
