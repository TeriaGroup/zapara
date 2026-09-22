using System.Net;
using System.Text;
using System.Text.Json;
using Zapara.Contracts.Sync;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserSyncTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Fresh_browser_receives_old_live_records_from_full_snapshot_after_retention()
    {
        using var server = new SyncServer();
        var (state, storage) = await Open(server);
        await using var cleanup = storage;

        await state.SynchronizeAsync(Ct);

        Assert.Equal("Задачи 1–3", state.Values<HomeworkValue>("homework").Single().Value.Text);
        Assert.Equal(400, state.Profile.AfterSequence);
        Assert.True(state.Profile.HasSnapshot);
        Assert.Equal(server.Epoch, state.Profile.SyncEpoch);
    }

    [Fact]
    public async Task Browser_mutation_keeps_the_entity_id_and_removes_outbox_only_after_server_ack()
    {
        using var server = new SyncServer();
        var (state, storage) = await Open(server);
        await using var cleanup = storage;
        await state.SynchronizeAsync(Ct);
        var id = Guid.NewGuid();
        await state.PutAsync("friend", id, new FriendValue("1", "О3313", "Маша", 1, true));
        Assert.Single(state.Profile.Outbox);

        await state.SynchronizeAsync(Ct);

        Assert.Empty(state.Profile.Outbox);
        Assert.Equal(id, state.Values<FriendValue>("friend").Single().Id);
        Assert.Equal("Маша", server.Records.Single(r => r.EntityId == id).Value is FriendValue f ? f.MemberNames : "");
        Assert.True(state.Values<FriendValue>("friend").Single().Revision > 400);
    }

    [Fact]
    public async Task Conflicting_offline_homework_preserves_both_versions_and_an_explicit_choice()
    {
        using var server = new SyncServer();
        var (state, storage) = await Open(server);
        await using var cleanup = storage;
        await state.SynchronizeAsync(Ct);
        var original = state.Values<HomeworkValue>("homework").Single();
        await state.PutAsync("homework", original.Id, original.Value with { });
        var local = new HomeworkValue(original.Value.SubjectRaw, original.Value.SubjectKey, "Мой черновик", 2, original.Value.CreatedAtUtc, original.Value.LegacyCreatedLocalDate);
        await state.PutAsync("homework", original.Id, local);
        server.ReplaceHomework("Изменено на телефоне");

        await state.SynchronizeAsync(Ct);

        Assert.Equal("Мой черновик", state.Values<HomeworkValue>("homework").Single().Value.Text);
        var conflict = Assert.Single(state.Conflicts);
        Assert.Equal("Изменено на телефоне", Assert.IsType<HomeworkValue>(conflict.ServerRecord!.Value).Text);
    }

    private static async Task<(WebAppState State, BrowserStorage Storage)> Open(SyncServer server)
    {
        var http = new HttpClient(server, disposeHandler: false) { BaseAddress = new("https://zapara.test/app/") };
        var storage = new BrowserStorage(new MemoryBrowser());
        var api = new BrowserApiClient(http, storage);
        var state = new WebAppState(http, storage, api);
        await state.InitializeAsync();
        return (state, storage);
    }

    [Theory]
    [InlineData(true, "Мой черновик")]
    [InlineData(false, "Изменено на телефоне")]
    public async Task Conflict_choice_is_explicit_and_converges_on_the_chosen_text(bool keepLocal, string wanted)
    {
        using var server = new SyncServer();
        var (state, storage) = await Open(server);
        await using var cleanup = storage;
        await state.SynchronizeAsync(Ct);
        var item = state.Values<HomeworkValue>("homework").Single();
        await state.PutAsync("homework", item.Id, new HomeworkValue(item.Value.SubjectRaw, item.Value.SubjectKey,
            "Мой черновик", 2, item.Value.CreatedAtUtc, item.Value.LegacyCreatedLocalDate));
        server.ReplaceHomework("Изменено на телефоне");
        await state.SynchronizeAsync(Ct);
        var conflict = Assert.Single(state.Conflicts);

        await state.ResolveConflictAsync(conflict.OpId, keepLocal, conflict.ServerRecord!.Revision, Ct);
        await state.SynchronizeAsync(Ct);

        Assert.Empty(state.Conflicts);
        Assert.Empty(state.Profile.Outbox);
        Assert.Equal(wanted, state.Values<HomeworkValue>("homework").Single().Value.Text);
        Assert.Equal(wanted, Assert.IsType<HomeworkValue>(server.Records.Single().Value).Text);
    }

    private sealed class SyncServer : HttpMessageHandler
    {
        public Guid Epoch { get; } = Guid.NewGuid();
        private readonly Guid family = Guid.NewGuid(), manifest = Guid.NewGuid();
        private readonly DateTimeOffset now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        private long sequence = 400;
        public List<SyncRecord> Records { get; } = [];
        private readonly List<SyncChange> changes = [];
        public SyncServer()
        {
            Records.Add(new("homework", Guid.NewGuid(), 5, false, now,
                new HomeworkValue("Математика", "математика", "Задачи 1–3", 1, now, new DateOnly(2026, 9, 21))));
        }
        private SyncMetadata Meta => new(Epoch, sequence, 300);
        public void ReplaceHomework(string text)
        {
            var old = Records.Single(r => r.EntityType == "homework");
            var value = (HomeworkValue)old.Value!;
            var updated = new SyncRecord("homework", old.EntityId, ++sequence, false, now,
                new HomeworkValue(value.SubjectRaw, value.SubjectKey, text, value.TargetNthOccurrence, value.CreatedAtUtc, value.LegacyCreatedLocalDate));
            Records.Remove(old); Records.Add(updated); changes.Add(new(sequence, Guid.NewGuid(), updated));
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/web-api/session") return BrowserApiClientTests.Session(family);
            if (path == "/web-api/sync/metadata") return Reply(Meta);
            if (path == "/web-api/sync/resync") return Reply(new SyncResyncManifest(manifest, Epoch, sequence, now, now.AddMinutes(10), Records.Count));
            if (path.StartsWith("/web-api/sync/resync/", StringComparison.Ordinal))
            {
                var header = new SyncResyncManifest(manifest, Epoch, sequence, now, now.AddMinutes(10), Records.Count);
                return Reply(new SyncResyncPage(header, 0, Records.Count, false, Records.Select((r, i) => new SyncManifestItem(i + 1, r)).ToArray()));
            }
            if (path == "/web-api/sync/changes")
            {
                var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(x => x.Split('=')).ToDictionary(x => x[0], x => x[1]);
                var after = long.Parse(query["afterSequence"]);
                var items = changes.Where(c => c.Sequence > after).ToArray();
                return Reply(new SyncChangesPage(Meta, after, items.LastOrDefault()?.Sequence ?? after, false, items));
            }
            if (path == "/web-api/sync/mutations")
            {
                var mutation = SyncJson.Parse<SyncMutation>(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
                var old = Records.FirstOrDefault(r => r.EntityType == mutation.EntityType && r.EntityId == mutation.EntityId);
                if ((old?.Revision ?? 0) != mutation.ExpectedRevision)
                    return Reply(new SyncMutationResult(409, "revision_conflict", Meta, old), HttpStatusCode.Conflict);
                var record = new SyncRecord(mutation.EntityType, mutation.EntityId, ++sequence, mutation.Action == "delete", now, mutation.Value);
                if (old is not null) Records.Remove(old);
                Records.Add(record); changes.Add(new(sequence, mutation.OpId, record));
                return Reply(new SyncMutationResult(200, "applied", Meta, record));
            }
            return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }
        private static HttpResponseMessage Reply<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new ByteArrayContent(SyncJson.Serialize(value)) };
    }
}
