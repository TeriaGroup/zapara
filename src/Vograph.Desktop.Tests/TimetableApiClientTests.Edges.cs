using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public partial class TimetableApiClientTests
{
    [Theory]
    [InlineData(".", "%2E")]
    [InlineData("..", "%2E%2E")]
    public async Task Dot_ids_cannot_change_route_or_escape_prefix(string id, string escaped)
    {
        using var handler = new FakeHttpHandler { Respond = r => Json(
            r.RequestUri!.AbsolutePath.EndsWith("/groups") ? Catalog(Pin, id) : Schedule(id)) };
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/prefix/"));
        await client.FetchAsync([id], TestContext.Current.CancellationToken);
        Assert.Equal($"https://example.invalid/prefix/api/v1/groups/{escaped}/timetable?snapshotId={Pin}",
            handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Invalid_utf8_is_a_sanitized_typed_failure()
    {
        var bytes = Encoding.UTF8.GetBytes(Catalog().ToJsonString());
        var marker = Encoding.UTF8.GetBytes("file");
        var index = bytes.AsSpan().IndexOf(marker);
        bytes[index] = 0xff;
        using var handler = new FakeHttpHandler { Respond = _ => FakeHttpHandler.Bytes(bytes) };
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        Assert.Equal(TimetableApiFailure.InvalidPayload,
            (await Assert.ThrowsAsync<TimetableApiException>(() => client.FetchAsync([], TestContext.Current.CancellationToken))).Failure);
    }

    [Fact]
    public async Task Rejects_depth_over_32_even_in_extensions()
    {
        var root = Catalog();
        JsonNode nested = new JsonObject();
        for (int i = 0; i < 34; i++) nested = new JsonObject { ["nested"] = nested };
        root["future"] = nested;
        using var handler = new FakeHttpHandler { Respond = _ => Json(root) };
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        await Assert.ThrowsAsync<TimetableApiException>(() => client.FetchAsync([], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Nullable_raw_and_remote_classroom_are_preserved()
    {
        var schedule = Schedule();
        var lesson = schedule["lessons"]![0]!;
        lesson["teacherRaw"] = null;
        lesson["classroomRaw"] = "Дистанционно";
        lesson["roomRaw"] = "";
        lesson["buildingRaw"] = "";
        using var handler = new FakeHttpHandler { Respond = r => Json(
            r.RequestUri!.AbsolutePath.EndsWith("/groups") ? Catalog() : schedule) };
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        var row = Assert.Single((await client.FetchAsync(["a"], TestContext.Current.CancellationToken)).DownloadedGroups["a"].Lessons);
        Assert.Null(row.TeacherRaw);
        Assert.Equal("Дистанционно", row.ToLesson("a").ClassroomRaw);
        Assert.Equal("", row.ToLesson("a").BuildingRaw);
    }

    [Fact]
    public async Task Failure_cancels_and_drains_inflight_siblings_without_publishing()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        using var handler = new TransportHandler(async (r, ct) =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("/groups")) return Json(Catalog(Pin, "a", "b"));
            if (r.RequestUri.AbsolutePath.Contains("/a/"))
            {
                await started.Task.WaitAsync(ct);
                return Text("secret", HttpStatusCode.ServiceUnavailable);
            }
            started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { cancelled = true; throw; }
            throw new InvalidOperationException();
        });
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        Assert.Equal(TimetableApiFailure.ServerUnavailable,
            (await Assert.ThrowsAsync<TimetableApiException>(() => client.FetchAsync(["a", "b"], TestContext.Current.CancellationToken))).Failure);
        Assert.True(cancelled);
    }

    [Fact]
    public async Task Overall_deadline_includes_retry_and_body_read_without_reset()
    {
        int catalogs = 0;
        using var stream = new GeneratedStream(1024, block: true);
        using var handler = new TransportHandler(async (r, ct) =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("/groups")) return Json(Catalog(++catalogs == 1 ? Pin : NewPin));
            if (catalogs == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return MissingPin();
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        });
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        var clock = Stopwatch.StartNew();
        Assert.Equal(TimetableApiFailure.Timeout,
            (await Assert.ThrowsAsync<TimetableApiException>(() => client.FetchAsync(["a"], TestContext.Current.CancellationToken))).Failure);
        Assert.InRange(clock.Elapsed.TotalSeconds, 29, 34);
        Assert.Equal(2, catalogs);
        Assert.True(stream.WasDisposed);
    }
}
