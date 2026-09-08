using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public partial class TimetableApiClientTests
{
    // Same assertions run with scripted HTTP normally and with an owned local Kestrel via the companion receipt.
    // No skipped/conditionally empty test and no server/Npgsql dependency in the Desktop graph.
    [Fact]
    public async Task Adoption_appservices_read_and_reopen_last_good()
    {
        var live = Environment.GetEnvironmentVariable("VOGRAPH_WINDOWS_API_PROOF_URL");
        if (live is not null)
        {
            Assert.True(Uri.TryCreate(live, UriKind.Absolute, out var url) && url.IsLoopback);
            Assert.Equal(live, Environment.GetEnvironmentVariable("VOGRAPH_API_BASE_URL"));
        }
        using var handler = new FakeHttpHandler { Respond = r =>
        {
            var catalog = r.RequestUri!.AbsolutePath.EndsWith("/groups");
            var json = catalog ? Catalog() : Schedule(r.RequestUri.AbsolutePath.Contains("/empty/") ? "empty" : "a");
            if (catalog) json["groups"]![1]!["name"] = "Empty friend";
            else if (json["group"]!["id"]!.GetValue<string>() == "empty") json["group"]!["name"] = "Empty friend";
            return Json(json);
        }};
        using var http = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "vograph-api-tests", Guid.NewGuid().ToString("N"));
        var source = live ?? "https://example.invalid/";
        Func<Uri, TimetableApiClient>? factory = live is null ? uri => new(http, uri) : null;
        string selected;
        string before;
        try
        {
            using (var app = AppServices.Create(dir, () => false, live is null ? source : null, factory))
            {
                Assert.True(await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
                Assert.Equal(2, app.Db.GetAllGroups().Count);
                selected = app.Db.GetAllGroups().Single(g => g.Id is not ("empty" or "9999")).Id;
                Assert.Null(new TimetableApiCache(app.Db).Read(selected));
                var settings = app.Db.GetSettings(); settings.MyGroupId = selected; app.Db.SaveSettings(settings);
                Assert.True(await app.Api.RefreshAsync(neededOnly: true, ct: TestContext.Current.CancellationToken));
                var lesson = Assert.Single(app.Db.GetAllLessonsForGroup(selected));
                if (live is not null)
                    Assert.Equal(Environment.GetEnvironmentVariable("VOGRAPH_WINDOWS_API_EXPECT_STALE") == "true", app.Api.SourceStale);
                Assert.Equal("09:00", lesson.TimeStart);
                Assert.Equal("10:35", lesson.TimeEnd);
                var friend = app.Db.GetAllGroups().Single(g => g.Id != selected);
                Assert.Null(new TimetableApiCache(app.Db).Read(friend.Id)?.Meta);
                app.Db.InsertFriend(new FriendGroup { GroupName = friend.Name, Enabled = true });
                Assert.True(await app.Api.RefreshAsync(neededOnly: true, ct: TestContext.Current.CancellationToken));
                Assert.NotNull(new TimetableApiCache(app.Db).Read(friend.Id)?.Meta);
                Assert.Empty(app.Db.GetAllLessonsForGroup(friend.Id));
                app.Overrides.AddOrUpdate(lesson.SubjectRaw, "global", "Personal title", "Personal note");
                var homework = app.Homework.AddHomework(lesson.SubjectRaw, "Personal homework", 1, new DateTime(2026, 9, 5));
                app.Homework.MarkDone(homework, true);
                var original = Assert.Single(app.Homework.GetAll());
                Assert.True(await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
                var retained = Assert.Single(app.Homework.GetAll());
                Assert.Equal(original.Id, retained.Id);
                Assert.Equal(original.Text, retained.Text);
                Assert.Equal(original.DoneAt, retained.DoneAt);
                Assert.Equal("done", retained.Status);
                Assert.Equal("Personal title", app.Overrides.GetDisplayName(lesson.SubjectRaw, 1));
                using var cmd = app.Db.Connection.CreateCommand();
                cmd.CommandText = "SELECT rawXml FROM groups WHERE id=@id"; cmd.Parameters.AddWithValue("@id", selected);
                Assert.Equal(DBNull.Value, cmd.ExecuteScalar());
                before = TimetableApiCacheTests.Dump(app.Db);
            }
            using (var app = AppServices.Create(dir, () => false, live is null ? source : null, factory))
            {
                app.AllowNetwork = false;
                Assert.False(await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
                Assert.Equal(before, TimetableApiCacheTests.Dump(app.Db));
                Assert.Single(app.Db.GetAllLessonsForGroup(selected));
                Assert.Single(app.Homework.GetAll());
                Assert.Equal("2026-09-01", app.Db.GetSettings().PeriodStart);
            }
            Console.WriteLine(live is null ? "surface=scripted HTTP + real AppServices/SQLite" : "surface=real loopback Kestrel + owned factory + AppServices/SQLite; reopen offline passed");
        }
        finally { CleanupApiDirectory(dir); }
    }
}
