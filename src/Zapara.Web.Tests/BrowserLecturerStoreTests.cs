using System.Net;
using System.Net.Http.Json;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserLecturerStoreTests
{
    private const string Xml = """
        <Timetable><Lecturer IdLecturer="one" LecturerName="Барт Елена Леонидовна" Kafedra="О6"><Days><Day Title="Понедельник"><LecturerLessons>
        <Lesson><WeekCode>1</WeekCode><Time>9:00 Нечетная</Time><Discipline>лек МАТЕМАТИКА</Discipline><Classroom>493*;</Classroom><Groups><Group><IdGroup>g</IdGroup><Number>А863С</Number></Group></Groups></Lesson>
        </LecturerLessons></Day></Days></Lecturer><Lecturer IdLecturer="two" LecturerName="Барт Алексей Алексеевич" Kafedra="Р7"/></Timetable>
        """;

    [Fact]
    public async Task Group_matching_is_reused_across_clock_renders_but_changes_with_group_names()
    {
        using var http = Client(request => request.RequestUri!.AbsolutePath.EndsWith(".xml") ? Text(Xml) : new(HttpStatusCode.NotFound));
        await using var storage = new BrowserStorage(new MemoryBrowser());
        var store = new BrowserLecturerStore(http, storage);
        await store.InitializeAsync();
        var first = store.MyTeacherIds([new Lesson { TeacherRaw = "Барт Е.Л." }]);
        Assert.Equal("one", Assert.Single(first));
        Assert.Same(first, store.MyTeacherIds([new Lesson { TeacherRaw = "барт е.л.; Барт Е.Л." }]));
        var second = store.MyTeacherIds([new Lesson { TeacherRaw = "Барт А.А." }]);
        Assert.Equal("two", Assert.Single(second));
        Assert.DoesNotContain("one", second);
    }

    [Fact]
    public async Task Abbreviated_teacher_deep_link_finds_the_corresponding_full_name()
    {
        var disk = new MemoryBrowser();
        using var http = Client(request => request.RequestUri!.AbsolutePath.EndsWith(".xml") ? Text(Xml) : new(HttpStatusCode.ServiceUnavailable));
        await using var storage = new BrowserStorage(disk);
        var store = new BrowserLecturerStore(http, storage);
        await store.InitializeAsync();
        Assert.Equal("one", Assert.Single(store.Search("Барт Е.Л.", false, [])).Id);
    }

    [Fact]
    public async Task Offline_bundle_search_matches_subject_and_only_mine_respects_initials()
    {
        var disk = new MemoryBrowser();
        using var http = Client(request => request.RequestUri!.AbsolutePath.EndsWith(".xml") ? Text(Xml) : new(HttpStatusCode.ServiceUnavailable));
        await using var storage = new BrowserStorage(disk);
        var store = new BrowserLecturerStore(http, storage);
        await store.InitializeAsync();

        Assert.Equal(2, store.Lecturers.Count);
        Assert.Equal("one", Assert.Single(store.Search("матем", true, [new Lesson { TeacherRaw = "Барт Е.Л." }])).Id);
        Assert.Empty(store.Search("", true, []));
        Assert.Single(store.LessonsOf("one", 2, invert: true));
        Assert.Empty(store.LessonsOf("one", 2));
        Assert.NotNull(disk.Load<string>("public", "lecturers:xml"));
    }

    [Fact]
    public async Task Invalid_refresh_does_not_replace_last_good_catalog()
    {
        var disk = new MemoryBrowser();
        var valid = true;
        using var http = Client(request => request.RequestUri!.AbsolutePath.EndsWith(".xml") ? Text(Xml) :
            Json(new LecturerCatalogEnvelope("v1", valid ? [new LecturerInfo { Id = "one", Name = "Барт Елена Леонидовна" }] : [])));
        await using var storage = new BrowserStorage(disk);
        var store = new BrowserLecturerStore(http, storage);
        await store.InitializeAsync();
        valid = false;
        await store.RefreshAsync();

        Assert.Equal("one", Assert.Single(store.Lecturers).Id);
        Assert.NotNull(store.Notice);
        Assert.Equal("one", Assert.Single(disk.Load<LecturerCatalogEnvelope>("public", "lecturers:catalog")!.Lecturers).Id);
    }

    [Fact]
    public async Task Mismatched_detail_version_keeps_saved_lessons_and_labels_them()
    {
        var disk = new MemoryBrowser();
        var version = "v1";
        using var http = Client(request => request.RequestUri!.AbsolutePath.EndsWith(".xml") ? Text(Xml) :
            request.RequestUri.AbsolutePath.EndsWith("/timetable") ? Json(new LecturerTimetableEnvelope("v1",
                new LecturerInfo { Id = "one", Name = "Барт Елена Леонидовна" },
                [new LecturerLesson { LecturerId = "one", DayOfWeek = 2, Parity = 2, TimeStart = "10:50", TimeEnd = "12:25", SubjectRaw = "Сохранённая пара" }])) :
            Json(new LecturerCatalogEnvelope(version, [new LecturerInfo { Id = "one", Name = "Барт Елена Леонидовна" }])));
        await using var storage = new BrowserStorage(disk);
        var store = new BrowserLecturerStore(http, storage);
        await store.InitializeAsync();
        await store.LoadLessonsAsync("one", TestContext.Current.CancellationToken);
        version = "v2";
        await store.RefreshAsync();
        await store.LoadLessonsAsync("one", TestContext.Current.CancellationToken);

        Assert.Equal("Сохранённая пара", Assert.Single(store.LessonsOf("one")).SubjectRaw);
        Assert.NotNull(store.DetailNotice("one"));
    }

    [Fact]
    public async Task Cached_catalog_and_selected_timetable_survive_a_fully_offline_restart()
    {
        var disk = new MemoryBrowser();
        disk.Store("public", "lecturers:xml", Xml);
        disk.Store("public", "lecturers:catalog", new LecturerCatalogEnvelope("v1", [new LecturerInfo { Id = "one", Name = "Барт Елена Леонидовна" }]));
        disk.Store("public", "lecturers:detail:one", new LecturerTimetableEnvelope("v1", new LecturerInfo { Id = "one", Name = "Барт Елена Леонидовна" },
            [new LecturerLesson { LecturerId = "one", DayOfWeek = 6, Parity = 0, TimeStart = "09:00", TimeEnd = "10:35", SubjectRaw = "Сохранённое занятие" }]));
        using var http = Client(_ => throw new HttpRequestException("offline"));
        await using var storage = new BrowserStorage(disk);
        var store = new BrowserLecturerStore(http, storage);
        await store.InitializeAsync();
        await store.LoadLessonsAsync("one", TestContext.Current.CancellationToken);

        Assert.Equal("one", Assert.Single(store.Lecturers).Id);
        Assert.Equal("Сохранённое занятие", Assert.Single(store.LessonsOf("one", 1)).SubjectRaw);
        Assert.Single(store.LessonsOf("one", 2));
        Assert.NotNull(store.DetailNotice("one"));
    }

    [Fact]
    public async Task Quota_failure_keeps_last_persisted_catalog_and_reports_live_data_not_saved()
    {
        var disk = new MemoryBrowser();
        var name = "Первый справочник";
        using var http = Client(request => request.RequestUri!.AbsolutePath.EndsWith(".xml") ? Text(Xml) :
            Json(new LecturerCatalogEnvelope("v1", [new LecturerInfo { Id = "one", Name = name }])));
        await using var storage = new BrowserStorage(disk);
        var store = new BrowserLecturerStore(http, storage);
        await store.InitializeAsync();
        disk.FailWrites = true;
        name = "Новый справочник";
        await store.RefreshAsync();

        Assert.Equal("Новый справочник", Assert.Single(store.Lecturers).Name);
        Assert.Equal("Первый справочник", Assert.Single(disk.Load<LecturerCatalogEnvelope>("public", "lecturers:catalog")!.Lecturers).Name);
        Assert.Contains("не сохранены", store.Notice!);
    }

    [Fact]
    public async Task Cancelled_teacher_request_never_publishes_its_late_response()
    {
        var disk = new MemoryBrowser();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new AsyncHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/timetable")) { started.SetResult(); return response.Task; }
            return Task.FromResult(request.RequestUri.AbsolutePath.EndsWith(".xml") ? Text(Xml) :
                Json(new LecturerCatalogEnvelope("v1", [new LecturerInfo { Id = "one", Name = "Барт Елена Леонидовна" }])));
        })) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(disk);
        var store = new BrowserLecturerStore(http, storage);
        await store.InitializeAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var loading = store.LoadLessonsAsync("one", cancellation.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        response.SetResult(Json(new LecturerTimetableEnvelope("v1", new LecturerInfo { Id = "one", Name = "Барт Елена Леонидовна" },
            [new LecturerLesson { LecturerId = "one", DayOfWeek = 1, Parity = 1, TimeStart = "09:00", TimeEnd = "10:35", SubjectRaw = "Опоздавший ответ" }])));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);

        Assert.Empty(store.LessonsOf("one"));
        Assert.Null(disk.Load<LecturerTimetableEnvelope>("public", "lecturers:detail:one"));
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> handler) => new(new Handler(handler)) { BaseAddress = new("https://zapara.test/app/") };
    private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handler(request));
    }
    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
