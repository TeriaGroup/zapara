using Vograph.Core.Services;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public partial class TimetableApiClientTests
{
    [Fact]
    public async Task Adoption_recomputes_due_but_derived_failure_does_not_rollback_raw_cache()
    {
        var day = 1;
        using var handler = new FakeHttpHandler { Respond = r =>
        {
            var catalog = r.RequestUri!.AbsolutePath.EndsWith("/groups");
            var json = catalog ? Catalog() : Schedule();
            if (!catalog) json["lessons"]![0]!["dayOfWeek"] = day;
            return Json(json);
        }};
        using var http = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "vograph-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var app = AppServices.Create(dir, () => false, "https://example.invalid/", uri => new(http, uri));
            var settings = app.Db.GetSettings(); settings.MyGroupId = "a"; app.Db.SaveSettings(settings);
            Assert.True(await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
            var raw = Assert.Single(app.Db.GetAllLessonsForGroup("a")).SubjectRaw;
            var id = app.Homework.AddHomework(raw, "Keep personal text", 1, new DateTime(2026, 9, 5));
            Assert.Equal(new DateTime(2026, 9, 7), app.Homework.GetById(id)!.DueDateComputed);
            day = 3;
            Assert.True(await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
            Assert.Equal(new DateTime(2026, 9, 9), app.Homework.GetById(id)!.DueDateComputed);
            using var cmd = app.Db.Connection.CreateCommand();
            cmd.CommandText = "CREATE TRIGGER fail_derived BEFORE UPDATE ON homework BEGIN SELECT RAISE(ABORT,'synthetic derived failure'); END";
            cmd.ExecuteNonQuery();
            day = 4;
            Assert.True(await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
            Assert.Equal(4, Assert.Single(app.Db.GetAllLessonsForGroup("a")).DayOfWeek);
            Assert.Equal(new DateTime(2026, 9, 9), app.Homework.GetById(id)!.DueDateComputed);
            Assert.Equal("Keep personal text", app.Homework.GetById(id)!.Text);
        }
        finally { CleanupApiDirectory(dir); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Adoption_late_response_after_stop_or_network_disable_never_commits(bool stop)
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new TransportHandler(async (_, _) =>
        {
            arrived.TrySetResult();
            await release.Task;
            return Json(Catalog());
        });
        using var http = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "vograph-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var app = AppServices.Create(dir, () => false, "https://example.invalid/", uri => new(http, uri));
            var before = TimetableApiCacheTests.Dump(app.Db);
            var task = app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken);
            await arrived.Task.WaitAsync(TestContext.Current.CancellationToken);
            if (stop) app.Api.Stop(); else app.AllowNetwork = false;
            release.SetResult();
            Assert.False(await task);
            Assert.Equal(before, TimetableApiCacheTests.Dump(app.Db));
        }
        finally { release.TrySetResult(); CleanupApiDirectory(dir); }
    }
}
