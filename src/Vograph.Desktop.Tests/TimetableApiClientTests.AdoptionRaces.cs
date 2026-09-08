using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public partial class TimetableApiClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Adoption_selection_or_friend_change_discards_old_response_and_follows_latest(bool friendChange)
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = 0;
        using var handler = new TransportHandler(async (r, ct) =>
        {
            if (Interlocked.Increment(ref blocked) == 1) { arrived.SetResult(); await release.Task.WaitAsync(ct); }
            var catalog = r.RequestUri!.AbsolutePath.EndsWith("/groups");
            var json = catalog ? Catalog() : Schedule(r.RequestUri.AbsolutePath.Contains("/empty/") ? "empty" : "a");
            if (catalog) json["groups"]![1]!["name"] = "Friend";
            else if (json["group"]!["id"]!.GetValue<string>() == "empty") json["group"]!["name"] = "Friend";
            return Json(json);
        });
        using var http = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "vograph-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var app = AppServices.Create(dir, () => false, "https://example.invalid/", uri => new(http, uri));
            var settings = app.Db.GetSettings(); settings.MyGroupId = "a"; app.Db.SaveSettings(settings);
            var run = app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken);
            await arrived.Task.WaitAsync(TestContext.Current.CancellationToken);
            await app.CoreGate.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                if (friendChange) app.Db.InsertFriend(new FriendGroup { GroupName = "Friend", Enabled = true });
                else { settings.MyGroupId = "empty"; app.Db.SaveSettings(settings); }
                // No explicit epoch bump: the captured identity comparison must independently catch this.
            }
            finally { app.CoreGate.Release(); }
            release.SetResult();
            Assert.True(await run);
            var cache = new TimetableApiCache(app.Db);
            Assert.NotNull(cache.Read("empty"));
            if (!friendChange) Assert.Null(cache.Read("a"));
            else Assert.NotNull(cache.Read("a"));
        }
        finally { release.TrySetResult(); CleanupApiDirectory(dir); }
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("cancel")]
    [InlineData("stop")]
    public async Task Adoption_offline_cancelled_or_stopped_never_sends_http(string mode)
    {
        using var handler = new FakeHttpHandler { Respond = _ => Json(Catalog()) };
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var dir = Path.Combine(Path.GetTempPath(), "vograph-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var app = AppServices.Create(dir, () => false, "https://example.invalid/", uri => new(http, uri));
            if (mode == "offline") app.AllowNetwork = false;
            if (mode == "cancel") cancellation.Cancel();
            if (mode == "stop") app.Api.Stop();
            Assert.False(await app.Api.RefreshAsync(ct: cancellation.Token));
            Assert.Empty(handler.Requests);
            Assert.Empty(app.Db.GetAllGroups());
        }
        finally { CleanupApiDirectory(dir); }
    }

    [Theory]
    [InlineData("503", TimetableApiFailure.ServerUnavailable)]
    [InlineData("bad", TimetableApiFailure.InvalidPayload)]
    [InlineData("missing", TimetableApiFailure.UnknownRequiredGroup)]
    public async Task Adoption_failure_preserves_entire_cache_and_reports_typed_stale(string failure, TimetableApiFailure expected)
    {
        var fail = false;
        using var handler = new FakeHttpHandler { Respond = r =>
        {
            if (fail && failure == "503") return Text("{}", System.Net.HttpStatusCode.ServiceUnavailable);
            if (fail && failure == "bad") return Text("{}");
            if (fail && failure == "missing") return Json(Catalog(Pin, "empty"));
            return Json(r.RequestUri!.AbsolutePath.EndsWith("/groups") ? Catalog() : Schedule());
        }};
        using var http = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "vograph-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var app = AppServices.Create(dir, () => false, "https://example.invalid/", uri => new(http, uri));
            var settings = app.Db.GetSettings(); settings.MyGroupId = "a"; app.Db.SaveSettings(settings);
            Assert.True(await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
            var before = TimetableApiCacheTests.Dump(app.Db);
            fail = true;
            Assert.False(await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
            Assert.Equal(before, TimetableApiCacheTests.Dump(app.Db));
            Assert.True(app.Api.SourceStale);
            Assert.Equal(expected, app.Api.LastFailure);
        }
        finally { CleanupApiDirectory(dir); }
    }
}
