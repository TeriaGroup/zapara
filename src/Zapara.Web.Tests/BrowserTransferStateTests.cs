using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using Xunit;
using Zapara.Contracts.Sync;
using Zapara.Web.Services;

namespace Zapara.Web.Tests;

public sealed class BrowserTransferStateTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static HomeworkValue Homework(string text) => new("Математика", "математика", text, 2, Created, new(2026, 9, 10));

    [Fact]
    public async Task GuestImportRetainsIdsCompletionAndGuestOriginalAndIsIdempotent()
    {
        await using var f = await Fixture.Account();
        var id = Guid.NewGuid(); var guest = new WebProfile();
        Add(guest, "homework", id, Homework("Гостевая запись")); Add(guest, "completion", id, new CompletionValue(true, Created.AddHours(1)));
        f.Disk.Store("profiles", "guest", guest);
        var original = JsonSerializer.Serialize(guest, BrowserStorage.Json);
        var source = LegacyTransferCodec.FromProfile(guest, "guest");
        var applied = await f.State.ApplyTransferAsync(await f.Preview(source), Ct);
        Assert.Equal(2, applied.Changed); Assert.Equal(id, f.State.Values<HomeworkValue>("homework").Single().Id);
        Assert.True(f.State.Values<CompletionValue>("completion").Single().Value.Done); Assert.Equal(2, f.State.Profile.Outbox.Count);
        Assert.Equal(original, JsonSerializer.Serialize(f.Disk.Load<WebProfile>("profiles", "guest"), BrowserStorage.Json));
        WebAppState.ValidateTransferBackup(f.Disk.Load<TransferBackup>("profiles", applied.BackupKey!), f.State.ProfileKey);
        Assert.Equal(0, (await f.State.ApplyTransferAsync(await f.Preview(source), Ct)).Changed);
        Assert.Equal(2, f.State.Profile.Outbox.Count);
    }

    [Fact]
    public async Task HomeworkCopyRemapsCompletionAndNeverOverwritesTheExistingHomework()
    {
        await using var f = await Fixture.Account(); var id = Guid.NewGuid();
        await f.State.PutManyAsync(new("homework", id, Homework("Текущая версия")), new("completion", id, new CompletionValue(false, null)));
        var guest = new WebProfile(); Add(guest, "homework", id, Homework("Другой вариант")); Add(guest, "completion", id, new CompletionValue(true, Created));
        var preview = await f.Preview(LegacyTransferCodec.FromProfile(guest, "guest"));
        preview.Rows.Single(row => row.Record.EntityType == "homework").Choice = "copy";
        await f.State.ApplyTransferAsync(preview, Ct);
        Assert.Equal("Текущая версия", f.State.Values<HomeworkValue>("homework").Single(value => value.Id == id).Value.Text);
        var copied = f.State.Values<HomeworkValue>("homework").Single(value => value.Id != id);
        Assert.Equal("Другой вариант", copied.Value.Text);
        Assert.True(f.State.Values<CompletionValue>("completion").Single(value => value.Id == copied.Id).Value.Done);
        Assert.False(f.State.Values<CompletionValue>("completion").Single(value => value.Id == id).Value.Done);
    }

    [Fact]
    public async Task OverrideReplacementIsExplicitAndKeepsCreationIdentityWithVerifiedBackup()
    {
        await using var f = Fixture.Guest(); var id = Guid.NewGuid();
        await f.State.PutAsync("override", id, new OverrideValue("лек высш. математика", "лек высш. математика", "global", "Старое имя", "Моя заметка", Created));
        var incoming = new OverrideValue("лек высш. мат.", "лек высш. мат.", "global", "Новое имя", "Из файла", Created.AddDays(3));
        var source = new TransferSource("file", [new("rename-source", "override", Guid.NewGuid(), incoming, "Название")], []);
        var preview = await f.Preview(source); Assert.Equal("conflict", Assert.Single(preview.Rows).Kind);
        Assert.Equal(0, (await f.State.ApplyTransferAsync(preview, Ct)).Changed);
        preview.Rows[0].Choice = "incoming"; var result = await f.State.ApplyTransferAsync(preview, Ct);
        var actual = Assert.Single(f.State.Values<OverrideValue>("override"));
        Assert.Equal(id, actual.Id); Assert.Equal(Created, actual.Value.CreatedAtUtc); Assert.Equal("Новое имя", actual.Value.DisplayName);
        var backup = f.Disk.Load<TransferBackup>("profiles", result.BackupKey!)!; WebAppState.ValidateTransferBackup(backup, "guest");
        var data = JsonSerializer.Deserialize<TransferBackupData>(backup.Payload, BrowserStorage.Json)!;
        Assert.Equal("Старое имя", Assert.IsType<OverrideValue>(LegacyTransferCodec.Decode(data.Records.Values.Single())).DisplayName);
    }

    [Fact]
    public async Task MetadataAndUnrelatedFreshEditsDoNotInvalidatePreviewButRelevantChangesDo()
    {
        await using var f = Fixture.Guest(); var id = Guid.NewGuid();
        var source = new TransferSource("file", [new("new-work", "homework", id, Homework("Импорт"), "Задание")], []);
        var preview = await f.Preview(source);
        var latest = new WebProfile { AfterSequence = 30, LastSyncedAt = Created, StorageRevision = 3 };
        Add(latest, "friend", Guid.NewGuid(), new FriendValue(null, "Другая группа", "Иван", 2, true)); f.Disk.Store("profiles", "guest", latest);
        await f.State.ApplyTransferAsync(preview, Ct);
        Assert.Single(f.State.Values<FriendValue>("friend")); Assert.Equal(30, f.State.Profile.AfterSequence);
        var stale = await f.Preview(new("file", [new("other-work", "homework", id, Homework("Из другого файла"), "Задание")], []));
        stale.Rows[0].Choice = "copy"; await f.State.PutAsync("homework", id, Homework("Изменено после просмотра"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.State.ApplyTransferAsync(stale, Ct));
        Assert.Single(f.State.Values<HomeworkValue>("homework"));
    }

    [Fact]
    public async Task BackupFailureBlocksCasAndAccountSwitchRejectsPreview()
    {
        await using var f = await Fixture.Account();
        var source = new TransferSource("guest", [new("row", "homework", Guid.NewGuid(), Homework("Не записывать"), "Задание")], []);
        var preview = await f.Preview(source); f.Bridge.FailBackup = true;
        await Assert.ThrowsAsync<JSException>(() => f.State.ApplyTransferAsync(preview, Ct));
        Assert.Empty(f.State.Profile.Records); Assert.Equal(0, f.Bridge.ProfileCommits);
        f.Bridge.FailBackup = false; f.Server.Family = Guid.NewGuid(); await f.Api!.RefreshSessionAsync(Ct);
        await Assert.ThrowsAsync<BrowserApiException>(() => f.State.ApplyTransferAsync(preview, Ct)); Assert.Empty(f.State.Profile.Records);
    }

    [Fact]
    public async Task FileRoundTripPreservesLegacyTimesAndDistinctDuplicatesWithoutCredentials()
    {
        await using var f = Fixture.Guest();
        var json = """
            {"Version":1,"ExportedAt":"2026-09-21T00:00:00Z","Overrides":[],"Homework":[
             {"SubjectRawNormalized":"Математика","Text":"Одинаково","CreatedAt":"2026-09-20T23:50:00","TargetNthOccurrence":2,"Status":"done","DoneAt":"2026-09-21T00:10:00","DueDateComputed":"2026-09-28T00:00:00"},
             {"SubjectRawNormalized":"Математика","Text":"Одинаково","CreatedAt":"2026-09-20T23:50:00","TargetNthOccurrence":2,"Status":"done","DoneAt":"2026-09-21T00:10:00","DueDateComputed":"2026-09-28T00:00:00"}],
             "Friends":[],"Settings":{"Language":"ru"}}
            """;
        await f.State.ApplyTransferAsync(await f.Preview(LegacyTransferCodec.Parse(Encoding.UTF8.GetBytes(json), Created)), Ct);
        var bytes = LegacyTransferCodec.Export(f.State.Profile, Created.AddDays(2)); using var exported = JsonDocument.Parse(bytes);
        Assert.Equal(1, exported.RootElement.GetProperty("Version").GetInt32()); Assert.Equal(2, exported.RootElement.GetProperty("Homework").GetArrayLength());
        Assert.Equal("2026-09-20T23:50:00", exported.RootElement.GetProperty("Homework")[0].GetProperty("CreatedAt").GetString());
        Assert.Equal("2026-09-21T00:10:00", exported.RootElement.GetProperty("Homework")[0].GetProperty("DoneAt").GetString());
        await f.State.ApplyTransferAsync(await f.Preview(LegacyTransferCodec.Parse(bytes, Created.AddDays(9))), Ct);
        Assert.Equal(2, f.State.Values<HomeworkValue>("homework").Count());
        Assert.DoesNotContain("Outbox", Encoding.UTF8.GetString(bytes)); Assert.DoesNotContain("accessToken", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task FriendLimitIncludesExistingRecordsAndSettingsNeedExplicitChoice()
    {
        await using var f = Fixture.Guest();
        for (var i = 0; i < 4; i++) await f.State.PutAsync("friend", Guid.NewGuid(), new FriendValue(null, "Группа " + i, "Имя", 1, true));
        var records = new List<TransferRecord>
        {
            new("friend1", "friend", Guid.NewGuid(), new FriendValue(null, "Новая 1", "Имя", 2, true), "Друг"),
            new("friend2", "friend", Guid.NewGuid(), new FriendValue(null, "Новая 2", "Имя", 3, true), "Друг"),
            new("settings", "settings", SyncValidation.SettingsId, new SettingsValue("А101", true, "19:00", null, 75, true), "Настройки")
        };
        var preview = await f.Preview(new("file", records, []));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.State.ApplyTransferAsync(preview, Ct)); Assert.Equal(4, f.State.Values<FriendValue>("friend").Count());
        preview.Rows.Single(row => row.Record.SourceKey == "friend2").Choice = "keep";
        await f.State.ApplyTransferAsync(preview, Ct);
        Assert.Equal(5, f.State.Values<FriendValue>("friend").Count()); Assert.Null(f.State.Settings.SelectedGroupId);
    }

    [Fact]
    public async Task CorruptBackupAndMissingInitialSnapshotCannotStartImport()
    {
        await using var f = await Fixture.Account();
        var source = new TransferSource("guest", [new("copy", "homework", Guid.NewGuid(), Homework("Исходное"), "Задание")], []);
        f.State.Profile.HasSnapshot = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Preview(source));
        f.State.Profile.HasSnapshot = true;
        var preview = await f.Preview(source);
        f.Bridge.CorruptBackup = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => f.State.ApplyTransferAsync(preview, Ct));
        Assert.Empty(f.State.Profile.Records); Assert.Equal(0, f.Bridge.ProfileCommits);
    }

    [Fact]
    public async Task CasRetryKeepsOperationIdsAndPreservesConcurrentUnrelatedRecords()
    {
        await using var f = await Fixture.Account(); var id = Guid.NewGuid();
        var guest = new WebProfile(); Add(guest, "homework", id, Homework("Перенос")); Add(guest, "completion", id, new CompletionValue(true, Created));
        var preview = await f.Preview(LegacyTransferCodec.FromProfile(guest, "guest"));
        f.Bridge.RaceOnce = true;
        await f.State.ApplyTransferAsync(preview, Ct);
        Assert.Equal(2, f.Bridge.CandidateOperations.Count);
        Assert.Equal(f.Bridge.CandidateOperations[0], f.Bridge.CandidateOperations[1]);
        Assert.Single(f.State.Values<FriendValue>("friend"));
        Assert.Equal(2, f.State.Profile.Outbox.Count);
    }

    [Fact]
    public async Task BackupRestorationUsesAnotherPreviewAndDoesNotRemoveNewerRecords()
    {
        await using var f = Fixture.Guest(); var id = Guid.NewGuid();
        var original = new OverrideValue("Физика", "физика", "global", "Исходное имя", null, Created);
        await f.State.PutAsync("override", id, original);
        var source = new TransferSource("file", [new("name", "override", Guid.NewGuid(), new OverrideValue("Физика", "физика", "global", "Импортированное имя", null, Created.AddDays(1)), "Название")], []);
        var preview = await f.Preview(source); preview.Rows[0].Choice = "incoming";
        var applied = await f.State.ApplyTransferAsync(preview, Ct);
        await f.State.PutAsync("homework", Guid.NewGuid(), Homework("После переноса"));
        var backup = f.Disk.Load<TransferBackup>("profiles", applied.BackupKey!)!;
        var body = JsonSerializer.Deserialize<TransferBackupData>(backup.Payload, BrowserStorage.Json)!;
        var restored = await f.Preview(LegacyTransferCodec.FromProfile(new() { Owner = body.Owner, Records = body.Records, ImportReceipts = body.Receipts }, "backup:" + backup.Id));
        Assert.Equal("Импортированное имя", f.State.Values<OverrideValue>("override").Single().Value.DisplayName);
        restored.Rows[0].Choice = "incoming"; await f.State.ApplyTransferAsync(restored, Ct);
        Assert.Equal("Исходное имя", f.State.Values<OverrideValue>("override").Single().Value.DisplayName);
        Assert.Single(f.State.Values<HomeworkValue>("homework"));
    }

    private static void Add(WebProfile profile, string type, Guid id, SyncValue value) => profile.Records[ProfileValues.Key(type, id)] = new()
    { EntityType = type, EntityId = id, Value = ProfileValues.Serialize(value) };
    private sealed class Fixture : IAsyncDisposable
    {
        internal MemoryBrowser Disk { get; } = new(); internal FaultBridge Bridge { get; }
        internal SessionServer Server { get; } = new(); private readonly HttpClient http;
        internal BrowserStorage Storage { get; } internal BrowserApiClient? Api { get; } internal WebAppState State { get; }
        private Fixture(bool account)
        { Bridge = new(Disk); http = new(Server) { BaseAddress = new("https://zapara.test/app/") }; Storage = new(Bridge); Api = account ? new(http, Storage) : null; State = new(http, Storage, Api); }
        internal static Fixture Guest() => new(false);
        internal static async Task<Fixture> Account()
        {
            var f = new Fixture(true); await f.State.InitializeAsync();
            f.State.Profile.HasSnapshot = true; f.State.Profile.SyncEpoch = Guid.NewGuid();
            f.Disk.Store("profiles", f.State.ProfileKey, f.State.Profile);
            return f;
        }
        internal Task<TransferPreview> Preview(TransferSource source) => State.PreviewTransferAsync(source, State.ProfileKey, State.Generation, Api?.Session.FamilyId, Ct);
        public async ValueTask DisposeAsync() { await Storage.DisposeAsync(); http.Dispose(); }
    }
    private sealed class SessionServer : HttpMessageHandler
    {
        internal Guid Family = Guid.NewGuid();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(request.RequestUri!.AbsolutePath == "/web-api/session" ? BrowserApiClientTests.Session(Family) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
    private sealed class FaultBridge(MemoryBrowser inner) : IJSRuntime, IJSObjectReference
    {
        internal bool FailBackup, CorruptBackup, RaceOnce; internal int ProfileCommits;
        internal List<Guid[]> CandidateOperations { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        {
            if (identifier == "import") return ValueTask.FromResult((TValue)(object)this);
            if (identifier == "write" && ((string?)args?[1])?.StartsWith("backup:") == true && FailBackup) throw new JSException("Quota");
            if (identifier == "read" && ((string?)args?[1])?.StartsWith("backup:") == true && CorruptBackup)
            {
                var value = inner.Load<TransferBackup>("profiles", (string)args[1]!);
                return ValueTask.FromResult((TValue)(object)JsonSerializer.Serialize(value! with { Sha256 = "wrong" }, BrowserStorage.Json));
            }
            if (identifier == "compareExchangeProfile")
            {
                ProfileCommits++;
                var candidate = JsonSerializer.Deserialize<WebProfile>((string)args![2]!, BrowserStorage.Json)!;
                CandidateOperations.Add(candidate.Outbox.Select(item => item.OpId).ToArray());
                if (RaceOnce)
                {
                    RaceOnce = false;
                    var owner = (string)args[0]!; var current = inner.Load<WebProfile>("profiles", owner)!;
                    current.StorageRevision++;
                    Add(current, "friend", Guid.NewGuid(), new FriendValue(null, "Параллельная запись", "Друг", 2, true));
                    inner.Store("profiles", owner, current);
                    return ValueTask.FromResult((TValue)(object)false);
                }
            }
            return inner.InvokeAsync<TValue>(identifier, ct, args);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
