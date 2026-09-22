using System.Net;
using System.Text.Json;
using Microsoft.JSInterop;
using Zapara.Contracts.Sync;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserSyncReconciliationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static HomeworkValue Homework(string text) => new("Математика", "математика", text, 1, Now, new(2026, 9, 21));

    [Theory]
    [InlineData("incremental", true)]
    [InlineData("snapshot", true)]
    [InlineData("absent-reset", true)]
    [InlineData("incremental", false)]
    [InlineData("snapshot", false)]
    [InlineData("absent-reset", false)]
    public async Task Deleted_homework_choice_preserves_saved_completion_only_when_restoring_local_copy(string delivery, bool keepLocal)
    {
        using var server = new Server(); var disk = new SharedDisk();
        var completedAt = Now.AddDays(-3).AddMinutes(-17);
        server.Records["completion"] = new("completion", server.Id, 6, false, completedAt, new CompletionValue(true, completedAt));
        server.Seed(disk);
        await using var tab = await Tab.Open(server, disk);
        await tab.State.PutAsync("homework", server.Id, Homework("Сохранить мою домашку"));
        Assert.Equal("homework", Assert.Single(tab.State.Profile.Outbox).EntityType);
        server.DeleteHomework();
        if (delivery == "snapshot") server.RetentionFloor = 10;
        if (delivery == "absent-reset") server.ResetEmpty();

        await tab.State.SynchronizeAsync(Ct, false);

        Assert.Equal(delivery == "incremental" ? 0 : 1, server.Resyncs);
        var conflict = Assert.Single(tab.State.Conflicts);
        if (delivery == "absent-reset") { Assert.Null(conflict.ServerRecord); Assert.Null(conflict.RelatedServerRecord); }
        else { Assert.True(conflict.ServerRecord!.Tombstone); Assert.True(conflict.RelatedServerRecord!.Tombstone); }
        var preserved = Assert.Single(tab.State.Values<CompletionValue>("completion"));
        Assert.True(preserved.Value.Done); Assert.Equal(completedAt, preserved.Value.DoneAtUtc);
        Assert.True(ProfileValues.Read<CompletionValue>(disk.Load<WebProfile>("profiles", server.Owner)!.Records[ProfileValues.Key("completion", server.Id)])!.Done);

        await tab.State.ResolveConflictAsync(conflict.OpId, keepLocal, conflict.ServerRecord?.Revision ?? 0, Ct);

        if (keepLocal)
        {
            var restored = Assert.Single(tab.State.Values<HomeworkValue>("homework"));
            Assert.NotEqual(server.Id, restored.Id);
            Assert.Equal("Сохранить мою домашку", restored.Value.Text);
            var completion = Assert.Single(tab.State.Values<CompletionValue>("completion"));
            Assert.Equal(restored.Id, completion.Id); Assert.True(completion.Value.Done); Assert.Equal(completedAt, completion.Value.DoneAtUtc);
            Assert.Equal(2, tab.State.Profile.Outbox.Count);
            await tab.State.SynchronizeAsync(Ct, false);
            Assert.Empty(tab.State.Profile.Outbox);
            Assert.Contains(server.Records.Values, r => r.EntityType == "homework" && r.EntityId == restored.Id && !r.Tombstone);
            var accepted = Assert.Single(server.Records.Values, r => r.EntityType == "completion" && r.EntityId == restored.Id);
            Assert.Equal(new CompletionValue(true, completedAt), Assert.IsType<CompletionValue>(accepted.Value));
        }
        else
        {
            Assert.Empty(tab.State.Values<HomeworkValue>("homework"));
            Assert.Empty(tab.State.Values<CompletionValue>("completion"));
            await tab.State.SynchronizeAsync(Ct, false);
            Assert.Empty(tab.State.Profile.Outbox);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delayed_ack_or_rejection_cannot_discard_newer_conflict_pulled_by_another_tab(bool reject)
    {
        using var server = new Server(); var disk = new SharedDisk(); server.Seed(disk);
        await using var a = await Tab.Open(server, disk); await using var b = await Tab.Open(server, disk);
        server.HoldChanges = true;
        var pulling = b.State.SynchronizeAsync(Ct, false);
        await server.ChangesStarted.Task.WaitAsync(Ct);
        if (reject) server.Replace("homework", Homework("Сервер 10"));
        await a.State.PutAsync("homework", server.Id, Homework("Мой текст"));
        server.HoldMutation = true;
        var sending = a.State.SynchronizeAsync(Ct, false);
        await server.MutationStarted.Task.WaitAsync(Ct);
        server.Replace("homework", Homework("Сервер 11"));
        server.ReleaseChanges.SetResult(); await pulling;
        Assert.Equal(11, Assert.Single(b.State.Conflicts).ServerRecord!.Revision);
        Assert.Equal(11, b.State.Profile.AfterSequence);
        server.ReleaseMutation.SetResult(); await sending;

        var conflict = Assert.Single(a.State.Conflicts);
        Assert.Equal(11, conflict.ServerRecord!.Revision);
        Assert.Equal("Сервер 11", Assert.IsType<HomeworkValue>(conflict.ServerRecord.Value).Text);
        Assert.Equal("Мой текст", Assert.Single(a.State.Values<HomeworkValue>("homework")).Value.Text);
        Assert.Equal(11, a.State.Profile.AfterSequence);
        Assert.Contains(11L, server.ReadCursors);
        var durable = disk.Load<WebProfile>("profiles", server.Owner)!;
        Assert.Equal(11, Assert.Single(durable.Outbox).ServerRecord!.Revision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Full_snapshot_preserves_independent_completion_for_either_explicit_choice(bool keepLocalCompletion)
    {
        using var server = new Server(); var disk = new SharedDisk(); server.Seed(disk, pendingHomework: true, olderRelated: true);
        await using var tab = await Tab.Open(server, disk);
        await tab.State.PutAsync("completion", server.Id, new CompletionValue(true, Now));
        server.Replace("homework", Homework("Текст снимка"));
        server.Replace("completion", new CompletionValue(false, null));
        server.RetentionFloor = 10;
        await tab.State.SynchronizeAsync(Ct, false);
        Assert.Equal(1, server.Resyncs);
        var parent = Assert.Single(tab.State.Conflicts, row => row.EntityType == "homework");
        Assert.Equal(11, parent.RelatedServerRecord!.Revision);
        Assert.True(Assert.Single(tab.State.Values<CompletionValue>("completion")).Value.Done);
        await tab.State.ResolveConflictAsync(parent.OpId, false, parent.ServerRecord!.Revision, Ct);
        Assert.True(Assert.Single(tab.State.Values<CompletionValue>("completion")).Value.Done);
        var completion = Assert.Single(tab.State.Conflicts);
        Assert.False(Assert.IsType<CompletionValue>(completion.ServerRecord!.Value).Done);
        await tab.State.ResolveConflictAsync(completion.OpId, keepLocalCompletion, 11, Ct);
        await tab.State.SynchronizeAsync(Ct, false);
        Assert.Equal(keepLocalCompletion, Assert.Single(tab.State.Values<CompletionValue>("completion")).Value.Done);
        Assert.Equal(keepLocalCompletion, Assert.IsType<CompletionValue>(server.Records["completion"].Value).Done);
        Assert.Empty(tab.State.Profile.Outbox);
    }

    [Fact]
    public async Task Delayed_snapshot_cannot_replace_a_newer_conflict_already_observed_in_shared_storage()
    {
        using var server = new Server(); var disk = new SharedDisk(); server.Seed(disk, pendingHomework: true, homeworkConflict: true);
        await using var a = await Tab.Open(server, disk); await using var b = await Tab.Open(server, disk);
        await a.State.ResolveConflictAsync(Assert.Single(a.State.Conflicts).OpId, true, 5, Ct);
        server.Replace("homework", Homework("Снимок 10"));
        server.Replace("completion", new CompletionValue(true, Now));
        server.RetentionFloor = 10; server.HoldSnapshot = true;
        var snapshot = b.State.SynchronizeAsync(Ct, false);
        await server.SnapshotStarted.Task.WaitAsync(Ct);
        server.Replace("homework", Homework("Новее снимка 12"));
        server.HoldChanges = true;
        var mutation = a.State.SynchronizeAsync(Ct, false);
        await server.ChangesStarted.Task.WaitAsync(Ct);
        Assert.Equal(12, Assert.Single(a.State.Conflicts).ServerRecord!.Revision);
        server.ReleaseSnapshot.SetResult(); await snapshot;
        var observedByB = Assert.Single(b.State.Conflicts).ServerRecord!;
        server.ReleaseChanges.SetResult(); await mutation;
        Assert.Equal(12, observedByB.Revision);
        Assert.Equal("Новее снимка 12", Assert.IsType<HomeworkValue>(observedByB.Value).Text);
    }

    [Fact]
    public async Task Ack_from_previous_epoch_cannot_replace_the_conflict_from_a_completed_shared_resync()
    {
        using var server = new Server(); var disk = new SharedDisk(); server.Seed(disk);
        await using var a = await Tab.Open(server, disk); await using var b = await Tab.Open(server, disk);
        server.HoldChanges = true;
        var pulling = b.State.SynchronizeAsync(Ct, false);
        await server.ChangesStarted.Task.WaitAsync(Ct);
        await a.State.PutAsync("homework", server.Id, Homework("Мой текст"));
        server.HoldMutation = true;
        var sending = a.State.SynchronizeAsync(Ct, false);
        await server.MutationStarted.Task.WaitAsync(Ct);
        server.ResetEpoch();
        server.ReleaseChanges.SetResult(); await pulling;
        Assert.Equal(server.Epoch, b.State.Profile.SyncEpoch);
        Assert.Equal(3, Assert.Single(b.State.Conflicts).ServerRecord!.Revision);
        server.ReleaseMutation.SetResult(); await sending;
        Assert.Equal(server.Epoch, a.State.Profile.SyncEpoch);
        var conflict = Assert.Single(a.State.Conflicts);
        Assert.Equal("sync_reset", conflict.ConflictCode);
        Assert.Equal(3, conflict.ServerRecord!.Revision);
        Assert.Equal("После сброса", Assert.IsType<HomeworkValue>(conflict.ServerRecord.Value).Text);
        Assert.Equal("Мой текст", Assert.Single(a.State.Values<HomeworkValue>("homework")).Value.Text);
        Assert.Equal(3, a.State.Profile.AfterSequence);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Accepting_homework_server_text_keeps_independent_completion_choices(bool keepLocalCompletion)
    {
        using var server = new Server(); var disk = new SharedDisk(); server.Seed(disk);
        await using var tab = await Tab.Open(server, disk);
        await tab.State.PutAsync("homework", server.Id, Homework("Моя домашка"));
        await tab.State.PutAsync("completion", server.Id, new CompletionValue(true, Now));
        server.Replace("homework", Homework("Домашка с телефона"));
        server.Replace("completion", new CompletionValue(false, null));
        await tab.State.SynchronizeAsync(Ct, false);
        var homeworkConflict = Assert.Single(tab.State.Conflicts, c => c.EntityType == "homework");
        Assert.True(Assert.Single(tab.State.Values<CompletionValue>("completion")).Value.Done);

        await tab.State.ResolveConflictAsync(homeworkConflict.OpId, false, homeworkConflict.ServerRecord!.Revision, Ct);

        Assert.Equal("Домашка с телефона", Assert.Single(tab.State.Values<HomeworkValue>("homework")).Value.Text);
        Assert.True(Assert.Single(tab.State.Values<CompletionValue>("completion")).Value.Done);
        var completionConflict = Assert.Single(tab.State.Conflicts);
        Assert.Equal("completion", completionConflict.EntityType);
        Assert.False(Assert.IsType<CompletionValue>(completionConflict.ServerRecord!.Value).Done);
        Assert.Equal(11, completionConflict.ServerRecord.Revision);
        await tab.State.ResolveConflictAsync(completionConflict.OpId, keepLocalCompletion, 11, Ct);
        await tab.State.SynchronizeAsync(Ct, false);
        Assert.Equal(keepLocalCompletion, Assert.Single(tab.State.Values<CompletionValue>("completion")).Value.Done);
        Assert.Equal(keepLocalCompletion, Assert.IsType<CompletionValue>(server.Records["completion"].Value).Done);
        Assert.Empty(tab.State.Profile.Outbox);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Full_snapshot_keeps_new_completion_when_only_homework_has_pending_changes(bool olderRelated)
    {
        using var server = new Server(); var disk = new SharedDisk(); server.Seed(disk, pendingHomework: true, olderRelated: olderRelated);
        await using var tab = await Tab.Open(server, disk);
        server.Replace("homework", Homework("Снимок с телефона"));
        server.Replace("completion", new CompletionValue(true, Now));
        server.RetentionFloor = 10;
        await tab.State.SynchronizeAsync(Ct, false);
        Assert.Equal(1, server.Resyncs);
        var conflict = Assert.Single(tab.State.Conflicts);
        Assert.True(Assert.Single(tab.State.Values<CompletionValue>("completion")).Value.Done);
        Assert.Equal(11, conflict.RelatedServerRecord!.Revision);
        Assert.True(Assert.IsType<CompletionValue>(conflict.RelatedServerRecord.Value).Done);
        await tab.State.ResolveConflictAsync(conflict.OpId, false, conflict.ServerRecord!.Revision, Ct);
        Assert.True(Assert.Single(tab.State.Values<CompletionValue>("completion")).Value.Done);
        Assert.Equal("Снимок с телефона", Assert.Single(tab.State.Values<HomeworkValue>("homework")).Value.Text);
        Assert.Equal(11, tab.State.Profile.AfterSequence);
        Assert.Empty(tab.State.Profile.Outbox);
        Assert.True(ProfileValues.Read<CompletionValue>(disk.Load<WebProfile>("profiles", server.Owner)!.Records[ProfileValues.Key("completion", server.Id)])!.Done);
    }

    private sealed class Tab : IAsyncDisposable
    {
        private readonly HttpClient http;
        private readonly BrowserStorage storage;
        private readonly BrowserApiClient api;
        public WebAppState State { get; }
        private Tab(Server server, SharedDisk disk)
        {
            http = new(server, false) { BaseAddress = new("https://zapara.test/app/") };
            storage = new(disk); api = new(http, storage); State = new(http, storage, api);
        }
        public static async Task<Tab> Open(Server server, SharedDisk disk) { var tab = new Tab(server, disk); await tab.State.InitializeAsync(); return tab; }
        public async ValueTask DisposeAsync() { await api.DisposeAsync(); await storage.DisposeAsync(); http.Dispose(); }
    }

    private sealed class Server : HttpMessageHandler
    {
        public Guid Epoch { get; private set; } = Guid.NewGuid();
        public Guid Id { get; } = Guid.NewGuid();
        private readonly Guid family = Guid.NewGuid(), manifest = Guid.NewGuid();
        public string Owner => "account@https://zapara.test#" + family.ToString("D");
        public Dictionary<string, SyncRecord> Records { get; } = [];
        private readonly List<SyncChange> changes = [];
        public List<long> ReadCursors { get; } = [];
        private long sequence = 9;
        public long RetentionFloor;
        public int Resyncs;
        public bool HoldChanges, HoldMutation, HoldSnapshot;
        private SyncResyncManifest? frozenManifest;
        private SyncRecord[] frozenRecords = [];
        public TaskCompletionSource ChangesStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseChanges = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource MutationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseMutation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SnapshotStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseSnapshot = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private SyncMetadata Meta => new(Epoch, sequence, RetentionFloor);
        public Server()
        {
            Records["homework"] = new("homework", Id, 5, false, Now, Homework("Исходная домашка"));
            Records["completion"] = new("completion", Id, 6, false, Now, new CompletionValue(false, null));
        }
        public void Seed(SharedDisk disk, bool pendingHomework = false, bool olderRelated = false, bool homeworkConflict = false)
        {
            var profile = new WebProfile { Owner = Owner, SyncEpoch = Epoch, HasSnapshot = true, AfterSequence = 9 };
            foreach (var record in Records.Values) profile.Records[ProfileValues.Key(record.EntityType, Id)] = new() { EntityType = record.EntityType, EntityId = Id, Revision = record.Revision, Value = ProfileValues.Serialize(record.Value!) };
            if (pendingHomework)
            {
                var value = ProfileValues.Serialize(Homework("Моя домашка"));
                profile.Records[ProfileValues.Key("homework", Id)].Value = value;
                profile.Outbox.Add(new() { EntityType = "homework", EntityId = Id, ExpectedRevision = 5, Value = value, Status = homeworkConflict ? "conflict" : "pending", ServerRecord = homeworkConflict ? Records["homework"] : null, RelatedServerRecord = olderRelated ? new("completion", Id, 8, false, Now, new CompletionValue(false, null)) : null });
            }
            disk.Store("profiles", Owner, profile);
        }
        public void Replace(string type, SyncValue value) => Store(new(type, Id, ++sequence, false, Now, value), Guid.NewGuid());
        public void DeleteHomework()
        {
            Store(new("homework", Id, ++sequence, true, Now, null), Guid.NewGuid());
            Store(new("completion", Id, ++sequence, true, Now, null), Guid.NewGuid());
        }
        public void ResetEmpty() { Epoch = Guid.NewGuid(); sequence = 0; RetentionFloor = 0; Records.Clear(); changes.Clear(); }
        public void ResetEpoch()
        {
            Epoch = Guid.NewGuid(); sequence = 3; RetentionFloor = 0; changes.Clear();
            Records["homework"] = new("homework", Id, 3, false, Now, Homework("После сброса"));
            Records["completion"] = new("completion", Id, 2, false, Now, new CompletionValue(false, null));
        }
        private string RecordKey(string type, Guid id) => id == Id ? type : ProfileValues.Key(type, id);
        private void Store(SyncRecord record, Guid op) { Records[RecordKey(record.EntityType, record.EntityId)] = record; changes.Add(new(record.Revision, op, record)); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/web-api/session") return BrowserApiClientTests.Session(family);
            if (path == "/web-api/sync/changes")
            {
                var after = long.Parse(request.RequestUri.Query.TrimStart('?').Split('&').Single(x => x.StartsWith("afterSequence=", StringComparison.Ordinal)).Split('=')[1]);
                ReadCursors.Add(after);
                if (HoldChanges) { HoldChanges = false; ChangesStarted.SetResult(); await ReleaseChanges.Task.WaitAsync(ct); }
                if (after < RetentionFloor || !request.RequestUri.Query.Contains(Epoch.ToString("D"), StringComparison.Ordinal)) return Reply(new SyncError(410, "sync_reset"), HttpStatusCode.Gone);
                var selected = changes.Where(c => c.Sequence > after).ToArray();
                return Reply(new SyncChangesPage(Meta, after, selected.LastOrDefault()?.Sequence ?? after, false, selected));
            }
            if (path == "/web-api/sync/mutations")
            {
                var mutation = SyncJson.Parse<SyncMutation>(await request.Content!.ReadAsByteArrayAsync(ct));
                if (mutation.SyncEpoch != Epoch) return Reply(new SyncError(410, "sync_reset"), HttpStatusCode.Gone);
                Records.TryGetValue(RecordKey(mutation.EntityType, mutation.EntityId), out var old);
                SyncMutationResult result;
                if ((old?.Revision ?? 0) != mutation.ExpectedRevision || old?.Tombstone == true ||
                    mutation.EntityType == "completion" && mutation.Action == "upsert" &&
                    (!Records.TryGetValue(RecordKey("homework", mutation.EntityId), out var parent) || parent.Tombstone))
                    result = new(409, "revision_conflict", Meta, old);
                else { var record = new SyncRecord(mutation.EntityType, mutation.EntityId, ++sequence, mutation.Action == "delete", Now, mutation.Value); Store(record, mutation.OpId); result = new(200, "applied", Meta, record); }
                if (HoldMutation) { HoldMutation = false; MutationStarted.SetResult(); await ReleaseMutation.Task.WaitAsync(ct); }
                return Reply(result, (HttpStatusCode)result.Status);
            }
            if (path == "/web-api/sync/resync") { Resyncs++; frozenRecords = Records.Values.ToArray(); frozenManifest = Manifest(); return Reply(frozenManifest); }
            if (path.StartsWith("/web-api/sync/resync/", StringComparison.Ordinal))
            {
                var page = new SyncResyncPage(frozenManifest!, 0, frozenRecords.Length, false, frozenRecords.Select((r, i) => new SyncManifestItem(i + 1, r)).ToArray());
                if (HoldSnapshot) { HoldSnapshot = false; SnapshotStarted.SetResult(); await ReleaseSnapshot.Task.WaitAsync(ct); }
                return Reply(page);
            }
            return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
        }
        private SyncResyncManifest Manifest() => new(manifest, Epoch, sequence, Now, Now.AddMinutes(10), Records.Count);
        private static HttpResponseMessage Reply<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new ByteArrayContent(SyncJson.Serialize(value)) };
    }

    private sealed class SharedDisk : IJSRuntime, IJSObjectReference
    {
        private readonly object gate = new();
        private readonly Dictionary<string, string> values = [];
        public void Store<T>(string store, string key, T value) { lock (gate) values[store + ":" + key] = JsonSerializer.Serialize(value, BrowserStorage.Json); }
        public T? Load<T>(string store, string key) { lock (gate) return values.TryGetValue(store + ":" + key, out var text) ? JsonSerializer.Deserialize<T>(text, BrowserStorage.Json) : default; }
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => InvokeAsync<T>(id, CancellationToken.None, args);
        public ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args)
        {
            lock (gate)
            {
                if (id == "import") return ValueTask.FromResult((T)(object)this);
                if (id == "read") return ValueTask.FromResult((T)(object?)values.GetValueOrDefault(args![0] + ":" + args[1])!);
                if (id == "write") values[args![0] + ":" + args[1]] = (string)args[2]!;
                if (id is "compareExchangeProfile" or "compareExchangeProfileSnapshot")
                {
                    var owner = (string)args![0]!; var revision = (long)args[1]!;
                    if ((Load<WebProfile>("profiles", owner)?.StorageRevision ?? 0) != revision) return ValueTask.FromResult((T)(object)false);
                    values["profiles:" + owner] = (string)args[2]!;
                    if (id == "compareExchangeProfileSnapshot") values["public:schedule"] = (string)args[3]!;
                    return ValueTask.FromResult((T)(object)true);
                }
                return ValueTask.FromResult(default(T)!);
            }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
