using System.Net;
using System.Text;
using Vograph.Desktop.Services;
using Vograph.Timetable;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ScheduleRefresherTests
{
    [Fact]
    public async Task Unchanged_meta_skips_lesson_download()
    {
        var handler = VoenmehHttp.Handler(meta: VoenmehHttp.OldMeta);
        using var refresher = new ScheduleRefresher(handler);

        var check = await refresher.CheckAsync(new[] { "А863С" }, "2026-09-05T10:00:00.0000000Z", TestContext.Current.CancellationToken);

        Assert.False(check.Modified);
        var meta = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, meta.Method);
        Assert.EndsWith("/api/schedule/meta", meta.RequestUri!.AbsolutePath);
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("/lessons", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Newer_meta_downloads_named_group_lessons()
    {
        var handler = VoenmehHttp.Handler();
        using var refresher = new ScheduleRefresher(handler);

        var check = await refresher.CheckAsync(new[] { "А863С" }, "2026-09-01T10:00:00.0000000Z", TestContext.Current.CancellationToken);

        Assert.True(check.Modified);
        Assert.Contains(check.Parsed!.Lessons, l => l.SubjectRaw == "лек ФИЛОСОФИЯ");
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/meta", StringComparison.Ordinal));
        Assert.Equal(1, handler.Requests.Count(r => r.RequestUri!.AbsolutePath.Contains("/lessons", StringComparison.Ordinal)));
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsoluteUri.Contains("TimetableGroup50.xml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_timestamp_means_full_download_without_skip()
    {
        var handler = VoenmehHttp.Handler();
        using var refresher = new ScheduleRefresher(handler);

        var check = await refresher.CheckAsync(null, TestContext.Current.CancellationToken);

        Assert.True(check.Modified);
        Assert.Equal(3, check.Parsed!.Groups.Count);
        Assert.Empty(check.Parsed.Lessons);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
        Assert.EndsWith("/meta", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Network_failure_propagates_to_the_caller()
    {
        var handler = new FakeHttpHandler { Respond = _ => throw new HttpRequestException("offline") };
        using var refresher = new ScheduleRefresher(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => refresher.CheckAsync(null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Html_meta_is_not_a_schedule()
    {
        var handler = new FakeHttpHandler { Respond = _ => FakeHttpHandler.Text("<!doctype html><html></html>") };
        using var refresher = new ScheduleRefresher(handler);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => refresher.CheckAsync(null, TestContext.Current.CancellationToken));
        Assert.Equal(TimetableParser.NotTimetable, ex.Message);
    }

    [Fact]
    public async Task Html_meta_falls_back_to_university_xml()
    {
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-timetable.xml"));
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(xml)).ToArray();
        var handler = new FakeHttpHandler
        {
            Respond = r =>
            {
                var uri = r.RequestUri ?? throw new InvalidOperationException("missing uri");
                if (uri.AbsolutePath.Contains("/api/schedule", StringComparison.Ordinal))
                    return FakeHttpHandler.Text(VoenmehHttp.CachedHtml);
                if (uri.AbsoluteUri.Contains("TimetableGroup50.xml", StringComparison.Ordinal))
                    return FakeHttpHandler.Bytes(utf16);
                throw new InvalidOperationException(uri.ToString());
            }
        };
        using var refresher = new ScheduleRefresher(handler);

        var check = await refresher.CheckAsync(new[] { "А863С" }, null, TestContext.Current.CancellationToken);

        Assert.True(check.Modified);
        Assert.Null(check.Parsed);
        Assert.Contains("<Timetable>", check.Xml, StringComparison.Ordinal);
        Assert.Contains("А863С", check.Xml, StringComparison.Ordinal);
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/meta", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsoluteUri.Contains("TimetableGroup50.xml", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("/lessons", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lessons_404_is_empty_not_failure()
    {
        var handler = new FakeHttpHandler
        {
            Respond = r => r.RequestUri!.AbsolutePath.EndsWith("/meta", StringComparison.Ordinal)
                ? FakeHttpHandler.Text(VoenmehHttp.Meta)
                : FakeHttpHandler.Text("missing", HttpStatusCode.NotFound)
        };
        using var refresher = new ScheduleRefresher(handler);

        var check = await refresher.CheckAsync(new[] { "А863С" }, null, TestContext.Current.CancellationToken);

        Assert.True(check.Modified);
        Assert.Empty(check.Parsed!.Lessons);
        Assert.Equal(new[] { "А863С" }, check.Parsed.FetchedGroupNames);
    }
}
