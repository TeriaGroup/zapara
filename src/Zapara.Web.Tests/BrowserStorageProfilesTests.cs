using System.Text.Json;
using Microsoft.JSInterop;
using Zapara.Contracts.Sync;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserStorageProfilesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid A = new("11111111-1111-4111-8111-111111111111");
    private static readonly Guid B = new("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ExistingOp = new("33333333-3333-4333-8333-333333333333");

    [Fact]
    public async Task Two_tabs_merge_independent_records_without_losing_any_outbox_entry()
    {
        var database = new CasMemory { PairFirstReads = true };
        database.Save(new WebProfile { Outbox = [new() { OpId = ExistingOp, EntityType = "homework" }] });
        await using var first = new BrowserStorage(database);
        await using var second = new BrowserStorage(database);
        await Task.WhenAll(first.UpdateProfileAsync("guest", p => AddFriend(p, A, "Маша"), Ct),
            second.UpdateProfileAsync("guest", p => AddFriend(p, B, "Петя"), Ct));
        var final = database.Profile();
        Assert.Equal(2, final.StorageRevision);
        Assert.Equal(2, final.Records.Count);
        Assert.Equal(new[] { A, B, ExistingOp }.Order(), final.Outbox.Select(x => x.OpId).Order());
        Assert.Equal("Маша", ProfileValues.Read<FriendValue>(final.Records[ProfileValues.Key("friend", A)])!.MemberNames);
        Assert.Equal("Петя", ProfileValues.Read<FriendValue>(final.Records[ProfileValues.Key("friend", B)])!.MemberNames);
        Assert.Equal(3, database.CompareCalls);
    }

    [Fact]
    public async Task Quota_failure_changes_neither_disk_nor_caller_revision()
    {
        var database = new CasMemory { QuotaFailure = true };
        var owned = AddFriend(new WebProfile { StorageRevision = 5 }, A, "Маша");
        database.Save(owned);
        await using var storage = new BrowserStorage(database);
        await Assert.ThrowsAsync<JSException>(() => storage.UpdateProfileAsync("guest", _ => owned, Ct));
        Assert.Equal(5, owned.StorageRevision);
        Assert.Equal(5, database.Profile().StorageRevision);
        Assert.Single(database.Profile().Records);
        Assert.Single(database.Profile().Outbox);
    }

    [Fact]
    public async Task Existing_or_transformed_owner_mismatch_is_refused_without_writing()
    {
        var database = new CasMemory();
        database.Save(new WebProfile { Owner = "account-other" }, "guest");
        await using var storage = new BrowserStorage(database);
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.UpdateProfileAsync("guest", p => throw new Xunit.Sdk.XunitException("Transform must not see another owner's profile."), Ct));
        database.Save(new WebProfile());
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.UpdateProfileAsync("guest", p => { p.Owner = "account-other"; return p; }, Ct));
        Assert.Equal(0, database.CompareCalls);
    }

    [Fact]
    public async Task Repeated_revision_races_are_bounded_without_overwriting_the_latest_profile()
    {
        var database = new CasMemory { AlwaysConflict = true };
        database.Save(new WebProfile { StorageRevision = 7 });
        await using var storage = new BrowserStorage(database);
        await Assert.ThrowsAsync<BrowserStorageConflictException>(() => storage.UpdateProfileAsync("guest", p => AddFriend(p, A, "Маша"), Ct));
        Assert.Equal(8, database.CompareCalls);
        Assert.Equal(7, database.Profile().StorageRevision);
        Assert.Empty(database.Profile().Records);
    }

    [Fact]
    public async Task Public_snapshot_changes_only_with_a_successful_profile_revision_compare()
    {
        var database = new CasMemory { Cache = "last-good" };
        var owned = new WebProfile { StorageRevision = 2 };
        database.Save(owned);
        var cache = new PublicCache(new(new(2026, 9, 1), 2, [], []), "Осень", false);
        await using var storage = new BrowserStorage(database);
        Assert.False(await storage.TryCommitSnapshotAsync("guest", 1, owned, cache, Ct));
        Assert.Equal("last-good", database.Cache);
        Assert.True(await storage.TryCommitSnapshotAsync("guest", 2, owned, cache, Ct));
        Assert.Equal(3, database.Profile().StorageRevision);
        Assert.Equal(2, owned.StorageRevision);
        Assert.Equal("Осень", JsonSerializer.Deserialize<PublicCache>(database.Cache!, BrowserStorage.Json)!.Title);
    }

    [Fact]
    public async Task Cancellation_after_sending_CAS_does_not_hide_a_successful_commit()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var database = new CasMemory { BeforeCompare = () => cancellation.Cancel() };
        await using var storage = new BrowserStorage(database);
        var committed = await storage.UpdateProfileAsync("guest", p => AddFriend(p, A, "Маша"), cancellation.Token);
        Assert.Equal(1, committed.StorageRevision);
        Assert.Single(database.Profile().Records);
    }

    private static WebProfile AddFriend(WebProfile profile, Guid id, string name)
    {
        profile.Records[ProfileValues.Key("friend", id)] = new() { EntityType = "friend", EntityId = id, Value = ProfileValues.Serialize(new FriendValue("group", "Группа", name, 1, true)) };
        profile.Outbox.Add(new() { OpId = id, EntityId = id, EntityType = "friend", Action = "upsert" });
        return profile;
    }

    private sealed class CasMemory : IJSRuntime, IJSObjectReference
    {
        private readonly Dictionary<string, string> profiles = [];
        private readonly object gate = new();
        private readonly TaskCompletionSource firstPair = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int reads;
        public bool PairFirstReads, QuotaFailure, AlwaysConflict;
        public int CompareCalls;
        public string? Cache;
        public Action? BeforeCompare;
        public void Save(WebProfile profile, string? key = null) { lock (gate) profiles[key ?? profile.Owner] = JsonSerializer.Serialize(profile, BrowserStorage.Json); }
        public WebProfile Profile(string owner = "guest") { lock (gate) return JsonSerializer.Deserialize<WebProfile>(profiles[owner], BrowserStorage.Json)!; }
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => InvokeAsync<T>(id, CancellationToken.None, args);
        public async ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args)
        {
            ct.ThrowIfCancellationRequested();
            if (id == "import") return (T)(object)this;
            if (id == "read")
            {
                string? value; int read;
                lock (gate) { value = profiles.GetValueOrDefault((string)args![1]!); read = ++reads; }
                if (PairFirstReads && read <= 2)
                {
                    if (read == 2) firstPair.SetResult();
                    await firstPair.Task.WaitAsync(Ct);
                }
                return (T)(object?)value!;
            }
            if (id is "compareExchangeProfile" or "compareExchangeProfileSnapshot")
            {
                BeforeCompare?.Invoke(); ct.ThrowIfCancellationRequested();
                lock (gate)
                {
                    CompareCalls++;
                    var owner = (string)args![0]!; var expected = (long)args[1]!; var json = (string)args[2]!;
                    var current = profiles.TryGetValue(owner, out var raw) ? JsonSerializer.Deserialize<WebProfile>(raw, BrowserStorage.Json)! : new WebProfile { Owner = owner };
                    var candidate = JsonSerializer.Deserialize<WebProfile>(json, BrowserStorage.Json)!;
                    if (current.Owner != owner || candidate.Owner != owner || candidate.StorageRevision != expected + 1) throw new JSException("Invalid owner or revision.");
                    if (AlwaysConflict || current.StorageRevision != expected) return (T)(object)false;
                    if (QuotaFailure) throw new JSException("QuotaExceededError");
                    profiles[owner] = json;
                    if (id == "compareExchangeProfileSnapshot") Cache = (string)args[3]!;
                    return (T)(object)true;
                }
            }
            throw new InvalidOperationException("Unexpected JS operation: " + id);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
