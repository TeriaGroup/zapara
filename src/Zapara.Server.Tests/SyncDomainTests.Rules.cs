using Xunit;
using Zapara.Contracts.Sync;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class SyncDomainTests
{
    [Fact]
    public async Task Friend_limit_counts_disabled_records_and_settings_has_one_reserved_identity()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var session = await Seed(accounts);
        var service = new SyncService(accounts, db.Configuration);
        var m = await service.MetadataAsync(session.AccessToken, Ct);
        Assert.NotNull(m);
        var requests = Enumerable.Range(0, 12).Select(i => new SyncMutation(m.SyncEpoch, Guid.NewGuid(), "friend", Guid.NewGuid(), 0,
            "upsert", new FriendValue(null, "Группа", $"{i}", 1, false))).ToArray();
        var results = await Task.WhenAll(requests.Select(r => service.MutateAsync(session.AccessToken, r, Ct)));
        Assert.Equal(5, results.Count(x => x.Status == 200));
        Assert.Equal(7, results.Count(x => x.Code == "friend_limit"));
        var failedIndex = Array.FindIndex(results, x => x.Status == 409);
        var removed = results.First(x => x.Status == 200).ServerRecord!;
        await service.MutateAsync(session.AccessToken, Delete(m, removed), Ct);
        Assert.Equal(SyncJson.Serialize(results[failedIndex]), SyncJson.Serialize(await service.MutateAsync(session.AccessToken, requests[failedIndex], Ct)));
        var fresh = requests[failedIndex];
        Assert.Equal(200, (await service.MutateAsync(session.AccessToken, new(m.SyncEpoch, Guid.NewGuid(), "friend", fresh.EntityId, 0, "upsert", fresh.Value), Ct)).Status);
        var settings = new SettingsValue(null, false, "09:00", null, 50, false);
        Assert.Throws<ArgumentException>(() => new SyncMutation(m.SyncEpoch, Guid.NewGuid(), "settings", Guid.NewGuid(), 0, "upsert", settings));
        var singletons = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => service.MutateAsync(session.AccessToken,
            new(m.SyncEpoch, Guid.NewGuid(), "settings", SyncValidation.SettingsId, 0, "upsert", settings), Ct)));
        Assert.Single(singletons, x => x.Status == 200);
        Assert.Throws<ArgumentException>(() => Homework(new string('a', 4001)));
        Assert.Throws<ArgumentException>(() => Homework("\ud800"));
        Assert.Throws<ArgumentException>(() => new HomeworkValue("Б1.О Математика", "математика", "x", 2, Created, null));
        var bounded = await service.MutateAsync(session.AccessToken, new(m.SyncEpoch, Guid.NewGuid(), "override", Guid.NewGuid(), 0, "upsert",
            new OverrideValue("x", "x", "global", new string('\u0001', 256), new string('\u0001', 4000), Created)), Ct);
        Assert.Equal(200, bounded.Status);
        Assert.InRange(SyncJson.Serialize(bounded.ServerRecord!).Length, 1, SyncValidation.RecordBytes);
    }

    [Fact]
    public async Task Creation_metadata_is_immutable_and_completion_is_private_with_atomic_homework_cascade()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var a = await Seed(accounts, "owner.a");
        var b = await Seed(accounts, "owner.b");
        var service = new SyncService(accounts, db.Configuration);
        var m = await service.MetadataAsync(a.AccessToken, Ct);
        Assert.NotNull(m);
        var id = Guid.NewGuid();
        var hw = await service.MutateAsync(a.AccessToken, Put(m, id), Ct);
        var immutable = new SyncMutation(m.SyncEpoch, Guid.NewGuid(), "homework", id, 1, "upsert", Homework(created: Created.AddDays(1)));
        var rejected = await service.MutateAsync(a.AccessToken, immutable, Ct);
        Assert.Equal("creation_metadata_immutable", rejected.Code);
        Assert.Equal(hw.ServerRecord, rejected.ServerRecord);
        Assert.Equal(SyncJson.Serialize(rejected), SyncJson.Serialize(await service.MutateAsync(a.AccessToken, immutable, Ct)));
        var completion = new SyncMutation(m.SyncEpoch, Guid.NewGuid(), "completion", id, 0, "upsert", new CompletionValue(true, Created));
        var done = await service.MutateAsync(a.AccessToken, completion, Ct);
        Assert.Equal(200, done.Status);
        var bm = await service.MetadataAsync(b.AccessToken, Ct);
        var foreign = await service.MutateAsync(b.AccessToken, new(bm.SyncEpoch, Guid.NewGuid(), "completion", id, 0, "upsert", completion.Value), Ct);
        Assert.Equal("revision_conflict", foreign.Code);
        Assert.Null(foreign.ServerRecord);
        var deletion = Delete(m, hw.ServerRecord!);
        var deleted = await service.MutateAsync(a.AccessToken, deletion, Ct);
        Assert.Equal("homework", deleted.ServerRecord!.EntityType);
        Assert.True(deleted.ServerRecord.Tombstone);
        Assert.Equal(4, deleted.Metadata.CurrentSequence);
        var changes = Value(await service.ChangesAsync(a.AccessToken, m.SyncEpoch, done.Metadata.CurrentSequence, 200, Ct));
        Assert.Equal(new[] { "homework", "completion" }, changes.Changes.Select(x => x.Record.EntityType));
        Assert.All(changes.Changes, c => { Assert.True(c.Record.Tombstone); Assert.Equal(deletion.OpId, c.OpId); Assert.Equal(2, c.Record.Revision); });
        Assert.Equal(SyncJson.Serialize(deleted), SyncJson.Serialize(await service.MutateAsync(a.AccessToken, deletion, Ct)));
        Assert.Equal(409, (await service.MutateAsync(a.AccessToken, new(m.SyncEpoch, Guid.NewGuid(), "completion", id, 2, "upsert", completion.Value), Ct)).Status);
        Assert.Equal(409, (await service.MutateAsync(a.AccessToken, new(m.SyncEpoch, Guid.NewGuid(), "completion", Guid.NewGuid(), 0, "upsert", completion.Value), Ct)).Status);
        var ov = new SyncMutation(m.SyncEpoch, Guid.NewGuid(), "override", Guid.NewGuid(), 0, "upsert", new OverrideValue("x", "x", "global", "x", null, Created));
        await service.MutateAsync(a.AccessToken, ov, Ct);
        Assert.Equal("creation_metadata_immutable", (await service.MutateAsync(a.AccessToken, new(m.SyncEpoch, Guid.NewGuid(), "override", ov.EntityId, 1,
            "upsert", new OverrideValue("x", "x", "global", "x", null, Created.AddDays(1))), Ct)).Code);
    }
}
