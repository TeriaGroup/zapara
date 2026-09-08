using System.Net;
using System.Net.Http.Headers;
using Vograph.Core.Services.Sync;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Profiles;
using Xunit;
using Zapara.Contracts.Sync;
using static Vograph.Desktop.Tests.AccountClientTestSupport;
using static Vograph.Desktop.Tests.PrivateSyncOutboxTests;

namespace Vograph.Desktop.Tests;

public sealed class PrivateSyncOutboxWorkerTests
{
    private static readonly DateTime Created = new(2026, 9, 5, 12, 0, 0);
    private static readonly Guid Epoch = Guid.Parse("0e0e0e0e-0e0e-4e0e-8e0e-0e0e0e0e0e0e");
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static string Access => Token("za_");

    [Fact]
    public async Task Conflict_keeps_local_draft_and_does_not_last_write_wins()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var id = app.Homework.AddHomework("лек ИСТОРИЯ", "локальный черновик", 1, Created);
        var pending = Assert.Single(app.Outbox.Pending());
        var conflicts = 0;
        PrivateSyncConflict? seen = null;
        app.PrivateSync!.Conflict += c => { conflicts++; seen = c; };
        using var http = new HttpClient(new Script((request, _) =>
        {
            if (request.Method == HttpMethod.Get) return Json(new SyncMetadata(Epoch, 9, 0));
            var server = new HomeworkValue("лек история", "лек история", "серверная версия", 1, Now, null);
            var record = new SyncRecord("homework", pending.EntityId, 4, false, Now, server);
            return Json(new SyncMutationResult(409, "revision_conflict", new SyncMetadata(Epoch, 9, 0), record), 409);
        }));
        using var client = new PrivateSyncHttpClient(http, new Uri("http://127.0.0.1/outbox-test/"));
        app.PrivateSync.Attach(client, _ => Task.FromResult(Access), background: false);
        await app.PrivateSync.PushPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal("локальный черновик", app.Homework.GetById(id)!.Text);
        Assert.Equal(0, app.Homework.GetById(id)!.Revision);
        Assert.Equal(1, conflicts);
        Assert.NotNull(seen);
        Assert.Equal(pending.EntityId, seen!.EntityId);
        Assert.Equal("homework", seen.EntityType);
        Assert.Contains("совпало", seen.Diagnostic, StringComparison.Ordinal);
        var draft = Assert.Single(app.Outbox.Drafts());
        Assert.Equal(pending.EntityId, draft.EntityId);
        Assert.Equal("conflict", Assert.Single(app.Outbox.Pending()).Status);
    }

    [Fact]
    public async Task Profile_switch_cancels_worker_and_ignores_late_success()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        app.Homework.AddHomework("лек ИСТОРИЯ", "глава 1", 1, Created);
        var pending = Assert.Single(app.Outbox.Pending());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Script(async (request, _) =>
        {
            if (request.Method == HttpMethod.Get) return Json(new SyncMetadata(Epoch, 1, 0));
            seen.TrySetResult();
            await release.Task;
            var mutation = SyncJson.Parse<SyncMutation>(await request.Content!.ReadAsByteArrayAsync());
            var record = new SyncRecord(mutation.EntityType, mutation.EntityId, 1, false, Now, mutation.Value);
            return Json(new SyncMutationResult(200, "applied", new SyncMetadata(mutation.SyncEpoch, 1, 0), record));
        }));
        using var client = new PrivateSyncHttpClient(http, new Uri("http://127.0.0.1/outbox-test/"));
        app.PrivateSync!.Attach(client, _ => Task.FromResult(Access), background: false);
        var push = app.PrivateSync.PushPendingAsync(TestContext.Current.CancellationToken);
        await seen.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        app.Work.Suspend();
        release.TrySetResult();
        await push.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(app.PrivateSync.IgnoredCallbacks >= 1);
        Assert.Equal(0, app.PrivateSync.AppliedCallbacks);
        Assert.Equal(0, app.Homework.GetAll()[0].Revision);
        Assert.Equal("pending", Assert.Single(app.Outbox.Pending()).Status);
        Assert.Equal(pending.OpId, app.Outbox.Pending()[0].OpId);
    }

    [Fact]
    public async Task Successful_mutate_acks_outbox_persists_revision_and_drops_draft()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var id = app.Homework.AddHomework("лек ИСТОРИЯ", "глава 1", 1, Created);
        var pending = Assert.Single(app.Outbox.Pending());
        using var seed = app.Db.Connection.CreateCommand();
        seed.CommandText = "INSERT INTO sync_draft(entityType, entityId, opId, localPayload, serverPayload, createdAtUtc) VALUES('homework', @id, @op, '{}', '{}', @ca)";
        seed.Parameters.AddWithValue("@id", pending.EntityId.ToString("D"));
        seed.Parameters.AddWithValue("@op", pending.OpId.ToString("D"));
        seed.Parameters.AddWithValue("@ca", Now.ToString("o"));
        seed.ExecuteNonQuery();
        using var http = new HttpClient(new Script(async (request, _) =>
        {
            if (request.Method == HttpMethod.Get) return Json(new SyncMetadata(Epoch, 1, 0));
            var mutation = SyncJson.Parse<SyncMutation>(await request.Content!.ReadAsByteArrayAsync());
            var record = new SyncRecord(mutation.EntityType, mutation.EntityId, mutation.ExpectedRevision + 1, false, Now, mutation.Value);
            return Json(new SyncMutationResult(200, "applied", new SyncMetadata(mutation.SyncEpoch, record.Revision, 0), record));
        }));
        using var client = new PrivateSyncHttpClient(http, new Uri("http://127.0.0.1/outbox-test/"));
        app.PrivateSync!.Attach(client, _ => Task.FromResult(Access), background: false);
        await app.PrivateSync.PushPendingAsync(TestContext.Current.CancellationToken);

        var hw = app.Homework.GetById(id)!;
        Assert.Equal(1, hw.Revision);
        Assert.False(hw.Tombstone);
        Assert.Empty(app.Outbox.Pending());
        Assert.Empty(app.Outbox.Drafts());
        Assert.Equal(1, app.PrivateSync.AppliedCallbacks);
    }

    [Fact]
    public async Task Mutate_410_aborts_push_so_second_pending_is_not_restamped_with_expired_epoch()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var id = app.Homework.AddHomework("лек ИСТОРИЯ", "глава 1", 1, Created);
        app.Homework.MarkDone(id, true);
        var pending = app.Outbox.Pending();
        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, r => r.EntityType == "homework");
        Assert.Contains(pending, r => r.EntityType == "completion");
        var oldEpoch = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var newEpoch = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var mutationEpochs = new List<Guid>();
        using var http = new HttpClient(new Script(async (request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/mutations", StringComparison.Ordinal))
            {
                var mutation = SyncJson.Parse<SyncMutation>(await request.Content!.ReadAsByteArrayAsync(ct));
                mutationEpochs.Add(mutation.SyncEpoch);
                return Json(new SyncMutationResult(410, "sync_reset", new SyncMetadata(newEpoch, 0, 0), null), 410);
            }
            if (path.Contains("/changes", StringComparison.Ordinal)) return Json(new SyncError(410, "sync_reset"), 410);
            if (request.Method == HttpMethod.Post && path.EndsWith("/resync", StringComparison.Ordinal))
                return Json(new SyncResyncManifest(Guid.Parse("11111111-1111-4111-8111-111111111111"), newEpoch, 0, Now, Now.AddMinutes(10), 0));
            if (path.Contains("/resync/", StringComparison.Ordinal))
            {
                var manifest = new SyncResyncManifest(Guid.Parse("11111111-1111-4111-8111-111111111111"), newEpoch, 0, Now, Now.AddMinutes(10), 0);
                return Json(new SyncResyncPage(manifest, 0, 0, false, Array.Empty<SyncManifestItem>()));
            }
            return Json(new SyncMetadata(oldEpoch, 0, 0));
        }));
        using var client = new PrivateSyncHttpClient(http, new Uri("http://127.0.0.1/outbox-test/"));
        app.PrivateSync!.Attach(client, _ => Task.FromResult(Access), background: false);
        await app.PrivateSync.PushPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { oldEpoch }, mutationEpochs);
        var afterPush = app.Outbox.Pending();
        Assert.Equal(2, afterPush.Count);
        Assert.DoesNotContain(afterPush, r => r.SyncEpoch == oldEpoch);
        var completion = Assert.Single(afterPush, r => r.EntityType == "completion");
        Assert.Null(completion.SyncEpoch);

        await app.PrivateSync.PullAsync(TestContext.Current.CancellationToken);
        Assert.Equal(newEpoch, app.Outbox.SyncEpoch);
        Assert.Equal(2, app.Outbox.Pending().Count);
        Assert.All(app.Outbox.Pending(), r => Assert.Null(r.SyncEpoch));
    }

    [Fact]
    public async Task Resync_clears_stamped_outbox_epoch_so_next_push_uses_the_new_epoch()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        app.Homework.AddHomework("лек ИСТОРИЯ", "черновик", 1, Created);
        var pending = Assert.Single(app.Outbox.Pending());
        var oldEpoch = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var newEpoch = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        app.Outbox.SetEpoch(oldEpoch, 0);
        using (var stamp = app.Db.Connection.CreateCommand())
        {
            stamp.CommandText = "UPDATE sync_outbox SET syncEpoch=@e WHERE opId=@id";
            stamp.Parameters.AddWithValue("@e", oldEpoch.ToString("D"));
            stamp.Parameters.AddWithValue("@id", pending.OpId.ToString("D"));
            stamp.ExecuteNonQuery();
        }
        Assert.Equal(oldEpoch, app.Outbox.Find(pending.OpId)!.SyncEpoch);
        Guid? pushedEpoch = null;
        using var http = new HttpClient(new Script(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/changes", StringComparison.Ordinal)) return Json(new SyncError(410, "sync_reset"), 410);
            if (request.Method == HttpMethod.Post && path.EndsWith("/resync", StringComparison.Ordinal))
                return Json(new SyncResyncManifest(Guid.Parse("11111111-1111-4111-8111-111111111111"), newEpoch, 3, Now, Now.AddMinutes(10), 0));
            if (path.Contains("/resync/", StringComparison.Ordinal))
            {
                var manifest = new SyncResyncManifest(Guid.Parse("11111111-1111-4111-8111-111111111111"), newEpoch, 3, Now, Now.AddMinutes(10), 0);
                return Json(new SyncResyncPage(manifest, 0, 0, false, Array.Empty<SyncManifestItem>()));
            }
            if (path.EndsWith("/mutations", StringComparison.Ordinal))
            {
                var mutation = SyncJson.Parse<SyncMutation>(await request.Content!.ReadAsByteArrayAsync());
                pushedEpoch = mutation.SyncEpoch;
                var record = new SyncRecord(mutation.EntityType, mutation.EntityId, 1, false, Now, mutation.Value);
                return Json(new SyncMutationResult(200, "applied", new SyncMetadata(mutation.SyncEpoch, 1, 0), record));
            }
            return Json(new SyncMetadata(oldEpoch, 3, 0));
        }));
        using var client = new PrivateSyncHttpClient(http, new Uri("http://127.0.0.1/outbox-test/"));
        app.PrivateSync!.Attach(client, _ => Task.FromResult(Access), background: false);
        await app.PrivateSync.PullAsync(TestContext.Current.CancellationToken);
        Assert.Equal(pending.OpId, Assert.Single(app.Outbox.Pending()).OpId);
        Assert.Null(app.Outbox.Find(pending.OpId)!.SyncEpoch);
        Assert.Equal(newEpoch, app.Outbox.SyncEpoch);

        await app.PrivateSync.PushPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(newEpoch, pushedEpoch);
        Assert.Empty(app.Outbox.Pending());
    }

    [Fact]
    public async Task Expired_cursor_resyncs_without_dropping_outbox_or_drafts()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var id = app.Homework.AddHomework("лек ИСТОРИЯ", "черновик", 1, Created);
        var pending = Assert.Single(app.Outbox.Pending());
        using var http = new HttpClient(new Script((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/metadata", StringComparison.Ordinal)) return Json(new SyncMetadata(Epoch, 3, 0));
            if (path.Contains("/changes", StringComparison.Ordinal)) return Json(new SyncError(410, "sync_reset"), 410);
            if (request.Method == HttpMethod.Post && path.EndsWith("/resync", StringComparison.Ordinal))
                return Json(new SyncResyncManifest(Guid.Parse("11111111-1111-4111-8111-111111111111"), Epoch, 3, Now, Now.AddMinutes(10), 1));
            var foreign = Guid.Parse("22222222-2222-4222-8222-222222222222");
            var tombstone = new SyncRecord("homework", foreign, 3, true, Now, null);
            var manifest = new SyncResyncManifest(Guid.Parse("11111111-1111-4111-8111-111111111111"), Epoch, 3, Now, Now.AddMinutes(10), 1);
            return Json(new SyncResyncPage(manifest, 0, 1, false, [new SyncManifestItem(1, tombstone)]));
        }));
        using var client = new PrivateSyncHttpClient(http, new Uri("http://127.0.0.1/outbox-test/"));
        app.PrivateSync!.Attach(client, _ => Task.FromResult(Access), background: false);
        using var cmd = app.Db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO sync_draft(entityType, entityId, opId, localPayload, serverPayload, createdAtUtc) VALUES('homework', @id, @op, '{}', '{}', @ca)";
        cmd.Parameters.AddWithValue("@id", pending.EntityId.ToString("D"));
        cmd.Parameters.AddWithValue("@op", pending.OpId.ToString("D"));
        cmd.Parameters.AddWithValue("@ca", Now.ToString("o"));
        cmd.ExecuteNonQuery();

        await app.PrivateSync.PullAsync(TestContext.Current.CancellationToken);

        Assert.Equal("черновик", app.Homework.GetById(id)!.Text);
        Assert.Equal(pending.OpId, Assert.Single(app.Outbox.Pending()).OpId);
        Assert.Single(app.Outbox.Drafts());
        Assert.Equal(Epoch, app.Outbox.SyncEpoch);
    }

    [Fact]
    public async Task Lan_export_does_not_include_account_tokens_or_account_db_path()
    {
        using var dir = new ProfileTestDirectory();
        using var guest = AppServices.Create(dir.Root, () => false);
        guest.AllowNetwork = false;
        using var account = guest.CreateProfile(ProfileDescriptor.Account(dir.Root,
            new Vograph.Core.Services.Accounts.AccountServerScope(new Uri("http://127.0.0.1/outbox-test/")).Key, UserId));
        account.AllowNetwork = false;
        account.Homework.AddHomework("лек ИСТОРИЯ", "za_ACCOUNT_SECRET", 1, Created);
        Assert.Contains("profiles", account.Profile.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => account.LanSync.Start());

        using var server = new LanSyncServer(guest, 0, localhostOnly: true);
        server.Start();
        using var http = new HttpClient();
        var body = await http.GetStringAsync($"http://127.0.0.1:{server.Port}/sync/", TestContext.Current.CancellationToken);
        Assert.DoesNotContain("za_", body, StringComparison.Ordinal);
        Assert.DoesNotContain(Access, body, StringComparison.Ordinal);
        Assert.DoesNotContain(account.Profile.DatabasePath, body, StringComparison.Ordinal);
        Assert.DoesNotContain("profiles\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profiles/", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("za_ACCOUNT_SECRET", body, StringComparison.Ordinal);
        server.Stop();
    }

    [Fact]
    public async Task Coordinator_logout_drains_worker_before_account_graph_closes()
    {
        await using var h = new ProfileHarness(factory: (guest, profile) =>
        {
            var created = guest.CreateProfile(profile);
            if (!profile.IsGuest)
            {
                var http = new HttpClient(new Script((request, _) =>
                {
                    if (request.Method == HttpMethod.Get) return Json(new SyncMetadata(Epoch, 1, 0));
                    return Json(new SyncError(503, "db_unavailable"), 503);
                }));
                created.PrivateSync!.Attach(new PrivateSyncHttpClient(http, new Uri("http://127.0.0.1/outbox-test/")),
                    _ => Task.FromResult(Access), background: false);
            }
            return created;
        });
        Assert.True((await h.Coordinator.LoginAsync("Test.User", Password, TestContext.Current.CancellationToken)).Committed);
        var account = h.Coordinator.Current.Services;
        account.Homework.AddHomework("лек ИСТОРИЯ", "глава 1", 1, Created);
        Assert.Single(account.Outbox.Pending());
        var push = account.PrivateSync!.PushPendingAsync(TestContext.Current.CancellationToken);
        await push.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True((await h.Coordinator.LogoutAsync(TestContext.Current.CancellationToken)).Committed);
        Assert.True(account.IsClosed);
        Assert.Equal(0, account.Work.Outstanding);
        Assert.True(h.Coordinator.Current.Services.Profile.IsGuest);
        Assert.Equal(0, CountOutbox(h.Coordinator.Current.Services.Db));
    }

    private sealed class Script : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action;
        public Script(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) => this.action = action;
        public Script(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> action)
            => this.action = (request, ct) => Task.FromResult(action(request, ct));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => this.action(request, cancellationToken);
    }

    private static HttpResponseMessage Json<T>(T value, int status = 200)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(SyncJson.Serialize(value)) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }
}
