using System.Net;
using System.Net.Http.Headers;
using Vograph.Core.Services.Sync;
using Zapara.Contracts.Sync;
using Xunit;
using static Vograph.Desktop.Tests.PrivateSyncOutboxTests;

namespace Vograph.Desktop.Tests;

public sealed class PrivateSyncStagingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 1, 30, 0, TimeSpan.Zero);
    private static readonly string Access = "za_" + new string('A', 43);

    [Fact]
    public async Task Interrupted_full_snapshot_keeps_old_visible_values_and_cursor_then_resumes_after_reopen()
    {
        using var dir = new ProfileTestDirectory();
        var oldEpoch = Guid.NewGuid();
        var epoch = Guid.NewGuid();
        var id = Guid.NewGuid();
        var friend = Guid.NewGuid();
        var initial = new HomeworkValue("Математика", "математика", "Старое полное состояние", 1, Now, null);
        var remote = new HomeworkValue("Математика", "математика", "Новое полное состояние", 2, Now, null);
        var manifest = new SyncResyncManifest(Guid.NewGuid(), epoch, 3, Now, Now.AddMinutes(10), 2);
        var record = new SyncRecord("homework", id, 2, false, Now, remote);
        var second = new SyncRecord("friend", friend, 3, false, Now, new FriendValue(null, "О732Б", "Друг", 2, true));
        var resume = false;
        var firstRequests = 0;
        using var transport = new HttpClient(new Script(request => {
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains("/changes")) return request.RequestUri.Query.Contains(epoch.ToString())
                ? Json(new SyncChangesPage(new(epoch, 3, 0), 3, 3, false, [])) : Json(new SyncError(410, "sync_reset"), 410);
            if (request.Method == HttpMethod.Post) return Json(manifest);
            if (path.Contains("afterOrdinal=0")) { firstRequests++; return Json(new SyncResyncPage(manifest, 0, 1, true, [new(1, record)])); }
            return resume ? Json(new SyncResyncPage(manifest, 1, 2, false, [new(2, second)])) : Json(new SyncError(503, "db_unavailable"), 503);
        }));
        using var client = new PrivateSyncHttpClient(transport, new Uri("http://127.0.0.1/"));
        using (var app = OpenAccount(dir.Root))
        {
            app.Outbox.ApplyLive(new("homework", id, 1, false, Now, initial));
            app.Outbox.SetEpoch(oldEpoch, 0);
            app.PrivateSync!.Attach(client, _ => Task.FromResult(Access));
            await app.PrivateSync.PullAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Старое полное состояние", Assert.Single(app.Homework.GetAll()).Text);
            Assert.Equal(1, app.Homework.GetAll()[0].Revision);
            Assert.Empty(app.Db.GetFriends());
            Assert.Equal(oldEpoch, app.Outbox.SyncEpoch);
            Assert.Equal(0, app.Outbox.AfterSequence);
        }
        resume = true;
        using (var app = OpenAccount(dir.Root))
        {
            app.PrivateSync!.Attach(client, _ => Task.FromResult(Access));
            await app.PrivateSync.PullAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Новое полное состояние", Assert.Single(app.Homework.GetAll()).Text);
            Assert.Equal(friend, Assert.Single(app.Db.GetFriends()).EntityId);
            Assert.Equal(epoch, app.Outbox.SyncEpoch);
            Assert.Equal(3, app.Outbox.AfterSequence);
            Assert.Equal(1, firstRequests);
        }
    }

    private sealed class Script(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(callback(request));
    }
    private static HttpResponseMessage Json<T>(T value, int status = 200)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(SyncJson.Serialize(value)) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }
}
