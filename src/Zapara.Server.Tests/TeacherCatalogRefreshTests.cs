using System.Net;
using System.Text;
using Xunit;
using Zapara.Client.Domain;
using Zapara.Server.Web;

namespace Zapara.Server.Tests;

public sealed class TeacherCatalogRefreshTests
{
    internal const string Xml = """
        <Timetable><Lecturer IdLecturer="one" LecturerName="Преподаватель" Kafedra="О6"><Days><Day Title="Понедельник"><LecturerLessons>
        <Lesson><WeekCode>1</WeekCode><Time>9:00 Нечетная</Time><Discipline>лек МАТЕМАТИКА</Discipline><Classroom>493</Classroom><Groups><Group><IdGroup>g</IdGroup><Number>А863С</Number></Group></Groups></Lesson>
        </LecturerLessons></Day></Days></Lecturer></Timetable>
        """;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    internal static TeacherCatalogStore Store(TimeProvider? clock = null) => new(
        new("packaged-hash", LecturerCatalog.Parse(Xml), new("packaged", null, null, null)), clock ?? TimeProvider.System);
    internal static HttpResponseMessage Response(HttpRequestMessage request, string xml, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { RequestMessage = request, Content = new StringContent(xml, Encoding.UTF8, "application/xml") };

    [Fact]
    public async Task Fixed_source_success_atomically_replaces_directory_and_lessons_and_records_fetch()
    {
        var store = Store();
        var before = store.Capture();
        using var http = new HttpClient(new InputHandler((request, _) =>
        {
            Assert.Equal("https://voenmeh.ru/wp-content/themes/Avada-Child-Theme-Voenmeh/_voenmeh_grafics/TimetableLecturer50.xml", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Response(request, Xml.Replace("one", "new")));
        }));
        Assert.True(await store.RefreshAsync(http, TimeSpan.FromSeconds(30), Ct));
        var after = store.Capture();
        Assert.Null(after.Teacher("one"));
        Assert.Equal("new", Assert.Single(after.Teachers.Lecturers).Id);
        Assert.Equal(after.Teachers.Version, after.Teacher("new")!.Version);
        Assert.Equal("new", Assert.Single(after.Teacher("new")!.Lessons).LecturerId);
        Assert.Equal("university", after.Metadata.Source);
        Assert.NotNull(after.Metadata.FetchedAt);
        Assert.Null(after.Metadata.LastFailure);
        Assert.Equal("one", Assert.Single(before.Teacher("one")!.Lessons).LecturerId);
    }

    [Theory]
    [InlineData("<html>upstream error</html>")]
    [InlineData("<Timetable/>")]
    [InlineData("<Timetable><Lecturer")]
    [InlineData("<!DOCTYPE Timetable [<!ENTITY x SYSTEM 'file:///secret'>]><Timetable>&x;</Timetable>")]
    [InlineData("")]
    [InlineData("bad-day")]
    [InlineData("missing-id")]
    [InlineData("duplicate-id")]
    [InlineData("bad-time")]
    [InlineData("bad-parity")]
    [InlineData("oversized")]
    public async Task Bad_source_preserves_last_good_and_exposes_only_safe_failure_code(string payload)
    {
        var store = Store();
        var next = Xml;
        using var http = new HttpClient(new InputHandler((request, _) => Task.FromResult(Response(request, next))));
        Assert.True(await store.RefreshAsync(http, TimeSpan.FromSeconds(30), Ct));
        var good = store.Capture();
        next = payload switch
        {
            "bad-day" => Xml.Replace("Понедельник", "unknown"),
            "missing-id" => Xml.Replace("IdLecturer=\"one\"", ""),
            "duplicate-id" => Xml.Replace("</Timetable>", "<Lecturer IdLecturer=\"one\" LecturerName=\"Other\"/></Timetable>"),
            "bad-time" => Xml.Replace("9:00", "99:99"),
            "bad-parity" => Xml.Replace("<WeekCode>1", "<WeekCode>garbage"),
            "oversized" => new string('x', 16 * 1024 * 1024 + 1),
            _ => payload
        };
        Assert.False(await store.RefreshAsync(http, TimeSpan.FromSeconds(30), Ct));
        var retained = store.Capture();
        Assert.Equal(good.Version, retained.Version);
        Assert.Same(good.Catalog, retained.Catalog);
        Assert.Equal(good.Metadata.FetchedAt, retained.Metadata.FetchedAt);
        Assert.Equal("source_rejected", retained.Metadata.LastFailure);
        Assert.NotEqual(good.RepresentationVersion, retained.RepresentationVersion);
    }

    [Theory]
    [InlineData("redirect")]
    [InlineData("followed")]
    [InlineData("compressed")]
    [InlineData("network")]
    public async Task Transport_failures_do_not_follow_or_publish_untrusted_content(string mode)
    {
        var store = Store();
        using var http = new HttpClient(new InputHandler((request, _) =>
        {
            if (mode == "network") throw new HttpRequestException("secret detail");
            var response = Response(request, Xml, mode == "redirect" ? HttpStatusCode.Redirect : HttpStatusCode.OK);
            if (mode == "followed") response.RequestMessage = new(HttpMethod.Get, "https://untrusted.invalid/data.xml");
            if (mode == "compressed") response.Content.Headers.ContentEncoding.Add("gzip");
            return Task.FromResult(response);
        }));
        Assert.False(await store.RefreshAsync(http, TimeSpan.FromSeconds(30), Ct));
        Assert.Equal("packaged", store.Capture().Metadata.Source);
        Assert.Equal("source_rejected", store.Capture().Metadata.LastFailure);
    }

    [Fact]
    public async Task Cancelled_refresh_leaves_a_coherent_snapshot_and_propagates_cancellation()
    {
        var store = Store();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var http = new HttpClient(new InputHandler((request, _) =>
        {
            cancel.Cancel();
            return Task.FromResult(Response(request, Xml.Replace("one", "late")));
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RefreshAsync(http, TimeSpan.FromSeconds(30), cancel.Token));
        Assert.Equal("packaged-hash", store.Capture().Version);
        Assert.Equal("cancelled", store.Capture().Metadata.LastFailure);
    }

    [Fact]
    public async Task Concurrent_refresh_is_skipped_and_readers_see_only_complete_generations()
    {
        var store = Store();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new InputHandler(async (request, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return Response(request, Xml.Replace("one", "next"));
        }));
        var refresh = store.RefreshAsync(http, TimeSpan.FromSeconds(30), Ct);
        await started.Task.WaitAsync(Ct);
        Assert.False(await store.RefreshAsync(http, TimeSpan.FromSeconds(30), Ct));
        Assert.Equal("one", Assert.Single(store.Capture().Teachers.Lecturers).Id);
        release.SetResult();
        Assert.True(await refresh);
        Assert.Equal("next", Assert.Single(store.Capture().Teacher("next")!.Lessons).LecturerId);
    }

    [Fact]
    public async Task Deadline_cancels_a_stalled_body_and_disposes_it_without_publishing()
    {
        var clock = new InputClock();
        var store = Store(clock);
        var stream = new InputStalledStream(clock.Expire);
        using var http = new HttpClient(new InputHandler((request, _) => Task.FromResult(InputHandler.Response(request, stream))));
        Assert.False(await store.RefreshAsync(http, TimeSpan.FromSeconds(17), Ct));
        Assert.Equal(TimeSpan.FromSeconds(17), clock.DueTime);
        Assert.True(stream.Disposed);
        Assert.Equal("source_timeout", store.Capture().Metadata.LastFailure);
        Assert.Equal("packaged-hash", store.Capture().Version);
    }

    [Fact]
    public async Task Real_packaged_schema_is_accepted_as_a_complete_upstream_document()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "public-data", "TimetableLecturer50.xml"), Ct);
        var parsed = TeacherCatalogInput.Validate(bytes);
        Assert.True(parsed.Lecturers.Count > 100);
        Assert.True(parsed.Lessons.Count > 1000);
    }

    [Fact]
    public async Task A_truncated_transport_body_cannot_publish_even_when_the_received_XML_is_complete()
    {
        var store = Store();
        using var http = new HttpClient(new InputHandler((request, _) =>
        {
            var result = Response(request, Xml);
            result.Content.Headers.ContentLength = Encoding.UTF8.GetByteCount(Xml) + 1;
            return Task.FromResult(result);
        }));
        Assert.False(await store.RefreshAsync(http, TimeSpan.FromSeconds(30), Ct));
        Assert.Equal("packaged-hash", store.Capture().Version);
    }
}
