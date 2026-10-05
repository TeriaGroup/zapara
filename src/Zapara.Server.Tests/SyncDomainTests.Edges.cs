using Xunit;
using Zapara.Contracts.Sync;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class SyncDomainTests
{
    [Fact]
    public async Task Completion_updates_are_independent_and_created_metadata_survives_precision_roundtrip()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var clock = new AccountClock { Now = Created.AddDays(7).AddTicks(1234567) };
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, clock);
        var session = await Seed(accounts);
        var service = new SyncService(accounts, db.Configuration);
        var m = await service.MetadataAsync(session.AccessToken, Ct);
        var request = Put(m, Guid.NewGuid());
        var hw = await service.MutateAsync(session.AccessToken, request, Ct);
        var completion = new SyncMutation(m.SyncEpoch, Guid.NewGuid(), "completion", request.EntityId, 0, "upsert", new CompletionValue(false, null));
        await service.MutateAsync(session.AccessToken, completion, Ct);
        var updated = await service.MutateAsync(session.AccessToken, new(m.SyncEpoch, Guid.NewGuid(), "completion", request.EntityId, 1, "upsert", new CompletionValue(true, clock.Now)), Ct);
        Assert.Equal(2, updated.ServerRecord!.Revision);
        var manifest = await service.BeginResyncAsync(session.AccessToken, Ct);
        var records = Value(await service.ReadResyncPageAsync(session.AccessToken, manifest.ManifestId, 0, 200, Ct)).Items.Select(x => x.Record).ToArray();
        Assert.Contains(hw.ServerRecord!, records);
        Assert.Contains(updated.ServerRecord, records);
        Assert.Equal(0, hw.ServerRecord!.ChangedAt.Ticks % 10);
        Assert.Equal(SyncJson.Serialize(hw), SyncJson.Serialize(await service.MutateAsync(session.AccessToken, request, Ct)));
        var conflict = await service.MutateAsync(session.AccessToken, new(m.SyncEpoch, Guid.NewGuid(), "homework", request.EntityId, 1, "upsert",
            new HomeworkValue("Б1.О Математика", "б1.о математика", "x", 2, Created, null)), Ct);
        Assert.Equal("creation_metadata_immutable", conflict.Code);
        Assert.Equal(hw.ServerRecord, conflict.ServerRecord);
    }

    [Fact]
    public async Task Invalid_requests_and_cancelled_calls_do_not_receipt_or_change_state()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var session = await Seed(accounts);
        var service = new SyncService(accounts, db.Configuration);
        var m = await service.MetadataAsync(session.AccessToken, Ct);
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.MutateAsync(session.AccessToken, null!, Ct));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.MutateAsync(session.AccessToken, Put(m, Guid.NewGuid()), cancelled.Token));
        Error(await service.ReadResyncPageAsync(session.AccessToken, Guid.NewGuid(), 0, 0, Ct), 400, "invalid_request");
        Assert.Equal(m, await service.MetadataAsync(session.AccessToken, Ct));
        Assert.Equal(0, await Count(db, "sync_receipts"));
        Assert.Equal(0, await Count(db, "sync_changes"));
        Assert.Equal(0, await Count(db, "sync_records"));
    }

    [Theory]
    [InlineData("sync_receipts")]
    [InlineData("sync_changes")]
    [InlineData("sync_records")]
    public async Task Every_retained_kind_independently_rotates_epoch_on_read_and_invalidates_manifest(string table)
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var clock = new AccountClock();
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, clock);
        var session = await Seed(accounts);
        var service = new SyncService(accounts, db.Configuration);
        var m = await service.MetadataAsync(session.AccessToken, Ct);
        var live = await service.MutateAsync(session.AccessToken, Put(m, Guid.NewGuid()), Ct);
        await service.MutateAsync(session.AccessToken, Delete(m, live.ServerRecord!), Ct);
        var manifest = await service.BeginResyncAsync(session.AccessToken, Ct);
        // Only one retention source eligible; clock remains the sole authoritative request time.
        var column = table == "sync_records" ? "changed_at" : "created_at";
        await db.Accounts.ExecuteAsync($"UPDATE {db.QuotedSchema}.{table} SET {column}={column}-interval '91 days'");
        clock.Now = clock.Now.AddDays(1);
        session = await accounts.LoginAsync(Login(), Ct);
        Error(await service.ChangesAsync(session.AccessToken, m.SyncEpoch, 0, 100, Ct), 410, "sync_reset");
        var fresh = await service.MetadataAsync(session.AccessToken, Ct);
        Assert.NotEqual(m.SyncEpoch, fresh.SyncEpoch);
        Assert.Equal(2, fresh.CurrentSequence);
        Assert.Equal(table == "sync_changes" ? 2 : 0, fresh.MinAfterSequence);
        Assert.Equal(0, await Count(db, table));
        Assert.Equal(0, await Count(db, "sync_manifests"));
        Assert.Equal(0, await Count(db, "sync_manifest_items"));
        Error(await service.ReadResyncPageAsync(session.AccessToken, manifest.ManifestId, 0, 100, Ct), 410, "manifest_expired");
    }

    [Theory]
    [InlineData("sync_changes")]
    [InlineData("sync_receipts")]
    public async Task Pruning_failure_rolls_back_epoch_and_manifest_invalidation(string table)
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var clock = new AccountClock();
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, clock);
        var session = await Seed(accounts);
        var service = new SyncService(accounts, db.Configuration);
        var m = await service.MetadataAsync(session.AccessToken, Ct);
        await service.MutateAsync(session.AccessToken, Put(m, Guid.NewGuid()), Ct);
        await service.BeginResyncAsync(session.AccessToken, Ct);
        await db.Accounts.ExecuteAsync($"""
            CREATE FUNCTION {db.QuotedSchema}.reject_prune() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic rejection'; END $$;
            CREATE TRIGGER reject_prune BEFORE DELETE ON {db.QuotedSchema}.{table}
            FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.reject_prune()
            """);
        clock.Now = clock.Now.AddDays(91);
        session = await accounts.LoginAsync(Login(), Ct);
        await Failure(AccountFailure.DbUnavailable, () => service.MetadataAsync(session.AccessToken, Ct));
        Assert.Equal(m.SyncEpoch, await db.Accounts.ScalarAsync<Guid>($"SELECT epoch FROM {db.QuotedSchema}.sync_state"));
        Assert.Equal(1, await Count(db, "sync_manifests"));
        Assert.Equal(1, await Count(db, "sync_manifest_items"));
        Assert.Equal(1, await Count(db, "sync_changes"));
        Assert.Equal(1, await Count(db, "sync_receipts"));
    }
}
