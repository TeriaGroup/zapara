using Xunit;
using Zapara.Contracts.Sync;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class SyncDomainTests
{
    [Fact]
    public async Task Delayed_commit_holds_sequence_order_and_blocks_pull_from_skipping_uncommitted_change()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var session = await Seed(accounts);
        var service = new SyncService(accounts, db.Configuration);
        var m = await service.MetadataAsync(session.AccessToken, Ct);
        var firstRequest = Put(m, Guid.NewGuid());
        await db.Accounts.ExecuteAsync($"""
            CREATE FUNCTION {db.QuotedSchema}.delay_owned() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.op_id='{firstRequest.OpId}'::uuid THEN PERFORM pg_sleep(2); END IF; RETURN NEW; END $$;
            CREATE TRIGGER delay_owned AFTER INSERT ON {db.QuotedSchema}.sync_receipts
            FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.delay_owned()
            """);
        var firstTask = service.MutateAsync(session.AccessToken, firstRequest, Ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_stat_activity WHERE wait_event='PgSleep' AND query LIKE '%{db.Schema}%' AND pid<>pg_backend_pid()") == 0)
            await Task.Delay(20, timeout.Token);
        var secondTask = service.MutateAsync(session.AccessToken, Put(m, Guid.NewGuid()), Ct);
        var pullTask = service.ChangesAsync(session.AccessToken, m.SyncEpoch, 0, 200, Ct);
        Assert.Equal(0, await Count(db, "sync_changes")); // First transaction has not committed.
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT sequence FROM {db.QuotedSchema}.sync_state"));
        while (await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_stat_activity WHERE wait_event_type='Lock' AND query LIKE '%{db.Accounts.Schema}%' AND pid<>pg_backend_pid()") < 2)
            await Task.Delay(20, timeout.Token);
        Assert.False(pullTask.IsCompleted);
        Assert.Equal(1, (await firstTask).Metadata.CurrentSequence);
        Assert.Equal(2, (await secondTask).Metadata.CurrentSequence);
        var pulled = Value(await pullTask);
        Assert.Equal(firstRequest.OpId, pulled.Changes[0].OpId);
        var remainder = Value(await service.ChangesAsync(session.AccessToken, m.SyncEpoch, pulled.NextAfterSequence, 200, Ct));
        Assert.Equal(new long[] { 1, 2 }, pulled.Changes.Concat(remainder.Changes).Select(x => x.Sequence));
    }

    [Fact]
    public async Task Homework_delete_rolls_back_when_associated_completion_write_fails()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var session = await Seed(accounts);
        var service = new SyncService(accounts, db.Configuration);
        var m = await service.MetadataAsync(session.AccessToken, Ct);
        var hw = await service.MutateAsync(session.AccessToken, Put(m, Guid.NewGuid()), Ct);
        await service.MutateAsync(session.AccessToken, new(m.SyncEpoch, Guid.NewGuid(), "completion", hw.ServerRecord!.EntityId, 0, "upsert", new CompletionValue(true, Created)), Ct);
        await db.Accounts.ExecuteAsync($"""
            CREATE FUNCTION {db.QuotedSchema}.reject_completion() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.entity_type='completion' THEN RAISE EXCEPTION 'synthetic rejection'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER reject_completion BEFORE UPDATE ON {db.QuotedSchema}.sync_records
            FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.reject_completion()
            """);
        await Failure(AccountFailure.DbUnavailable, () => service.MutateAsync(session.AccessToken, Delete(m, hw.ServerRecord), Ct));
        Assert.Equal(2, (await service.MetadataAsync(session.AccessToken, Ct)).CurrentSequence);
        Assert.Equal(2, await Count(db, "sync_receipts"));
        Assert.Equal(2, await Count(db, "sync_changes"));
        var manifest = await service.BeginResyncAsync(session.AccessToken, Ct);
        Assert.All(Value(await service.ReadResyncPageAsync(session.AccessToken, manifest.ManifestId, 0, 200, Ct)).Items,
            i => { Assert.False(i.Record.Tombstone); Assert.Equal(1, i.Record.Revision); });
    }
}
