using System.Net;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public partial class TimetableApiClientTests
{
    [Fact]
    public async Task Fetches_pinned_distinct_groups_including_explicit_empty_and_preserves_raw()
    {
        var requests = new List<string>();
        using var handler = new FakeHttpHandler { Respond = r =>
        {
            lock (requests) requests.Add(r.RequestUri!.PathAndQuery);
            return Json(r.RequestUri!.AbsolutePath.EndsWith("/groups") ? Catalog() :
                Schedule(r.RequestUri.AbsolutePath.Contains("/empty/") ? "empty" : "a"));
        }};
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/prefix"));
        var result = await client.FetchAsync(new[] { "empty", "a", "a" }, TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Groups.Length);
        Assert.Equal(2, result.DownloadedGroups.Count);
        Assert.Empty(result.DownloadedGroups["empty"].Lessons);
        var row = Assert.Single(result.DownloadedGroups["a"].Lessons);
        Assert.Equal("ЛЕК Ёлка  Тест", row.SubjectRaw);
        Assert.Equal("493*", row.ToLesson("a").ClassroomRaw);
        row.ToLesson("a").SubjectRaw = "changed";
        result.Groups[0].ToGroup().Name = "changed";
        Assert.Equal("ЛЕК Ёлка  Тест", row.SubjectRaw);
        Assert.Equal("ТЕСТ-ГРУППА", result.Groups[0].Name);
        Assert.Equal("/prefix/api/v1/groups", requests[0]);
        Assert.Equal(new[] { $"/prefix/api/v1/groups/a/timetable?snapshotId={Pin}",
            $"/prefix/api/v1/groups/empty/timetable?snapshotId={Pin}" }, requests.Skip(1).Order());
    }

    [Fact]
    public async Task Catalog_only_leaves_all_groups_unfetched()
    {
        using var handler = new FakeHttpHandler { Respond = _ => Json(Catalog()) };
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("http://[::1]:1234/"));
        var result = await client.FetchAsync([], TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Groups.Length);
        Assert.Empty(result.DownloadedGroups);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Unknown_required_group_fails_before_schedule_download()
    {
        using var handler = new FakeHttpHandler { Respond = _ => Json(Catalog()) };
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        var error = await Assert.ThrowsAsync<TimetableApiException>(() => client.FetchAsync(["absent"], TestContext.Current.CancellationToken));
        Assert.Equal(TimetableApiFailure.UnknownRequiredGroup, error.Failure);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task Missing_generation_retries_entire_catalog_only_once(bool alwaysMissing, int catalogsExpected)
    {
        var catalogs = 0;
        using var handler = new FakeHttpHandler { Respond = r =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("/groups"))
                return Json(Catalog(++catalogs == 1 ? Pin : NewPin));
            return catalogs == 1 || alwaysMissing ? MissingPin() : Json(Schedule(pin: NewPin));
        }};
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        if (alwaysMissing)
            Assert.Equal(TimetableApiFailure.SnapshotUnavailable,
                (await Assert.ThrowsAsync<TimetableApiException>(() => client.FetchAsync(["a"], TestContext.Current.CancellationToken))).Failure);
        else
            Assert.Equal(Guid.Parse(NewPin), (await client.FetchAsync(["a"], TestContext.Current.CancellationToken)).DownloadedGroups["a"].Meta.SnapshotId);
        Assert.Equal(catalogsExpected, catalogs);
    }

    [Theory]
    [InlineData("http://example.invalid/")]
    [InlineData("https://user:secret@example.invalid/")]
    [InlineData("https://example.invalid/?token=secret")]
    [InlineData("https://example.invalid/#secret")]
    [InlineData("file:///tmp/")]
    public void Rejects_unsafe_base_uri(string uri)
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentException>(() => new TimetableApiClient(http, new Uri(uri)));
    }

    [Theory]
    [InlineData(503, "{\"code\":\"snapshot_not_found\",\"status\":503}")]
    [InlineData(404, "{\"code\":\"group_not_found\",\"status\":404}")]
    [InlineData(404, "{\"code\":\"snapshot_not_found\",\"status\":503}")]
    [InlineData(404, "<html>secret</html>")]
    public async Task Other_errors_never_retry_or_return_partial_output(int status, string body)
    {
        var catalogs = 0;
        using var handler = new FakeHttpHandler { Respond = r => r.RequestUri!.AbsolutePath.EndsWith("/groups")
            ? (++catalogs > 0 ? Json(Catalog()) : throw new Exception()) : Text(body, (HttpStatusCode)status) };
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        var error = await Assert.ThrowsAsync<TimetableApiException>(() => client.FetchAsync(["a"], TestContext.Current.CancellationToken));
        Assert.Equal(1, catalogs);
        Assert.DoesNotContain("secret", error.ToString());
        Assert.DoesNotContain("example.invalid", error.ToString());
    }
}
