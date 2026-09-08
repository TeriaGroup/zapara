using Xunit;
using Zapara.Contracts.Sync;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class SyncDomainTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Created = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static HomeworkValue Homework(string text = "Задание", DateTimeOffset? created = null)
        => new("Б1.О Математика", "б1.о математика", text, 2, created ?? Created, new DateOnly(2026, 8, 31));
    private static SyncMutation Put(SyncMetadata metadata, Guid id, long revision = 0, string text = "Задание", Guid? op = null)
        => new(metadata.SyncEpoch, op ?? Guid.NewGuid(), "homework", id, revision, "upsert", Homework(text));
    private static SyncMutation Delete(SyncMetadata metadata, SyncRecord record, Guid? op = null)
        => new(metadata.SyncEpoch, op ?? Guid.NewGuid(), record.EntityType, record.EntityId, record.Revision, "delete", null);
    private static T Value<T>(SyncReadResult<T> result) where T : class
    {
        Assert.NotNull(result);
        Assert.Null(result.Error);
        return Assert.IsType<T>(result.Value);
    }
    private static void Error<T>(SyncReadResult<T> result, int status, string code) where T : class
    {
        Assert.NotNull(result);
        Assert.Null(result.Value);
        Assert.Equal(new SyncError(status, code), result.Error);
    }
    private static Task<long> Count(SyncPostgresFixture db, string table)
        => db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.{table}");

    [Fact]
    public async Task Metadata_is_lazy_persistent_and_retry_receipts_precede_CAS()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        TestContext.Current.TestOutputHelper!.WriteLine($"Owned SQL schemas: sync={db.Schema}; accounts={db.Accounts.Schema}");
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var session = await Seed(accounts);
        var service = new SyncService(accounts, db.Configuration);
        Assert.Equal(0, await Count(db, "sync_state"));
        var metadata = await service.MetadataAsync(session.AccessToken, Ct);
        Assert.NotNull(metadata);
        Assert.Equal(0, metadata.CurrentSequence);
        Assert.Equal(metadata, await new SyncService(accounts, db.Configuration).MetadataAsync(session.AccessToken, Ct));
        var request = Put(metadata, Guid.NewGuid());
        var applied = await service.MutateAsync(session.AccessToken, request, Ct);
        Assert.NotNull(applied);
        Assert.Equal(200, applied.Status);
        var conflictRequest = Put(metadata, request.EntityId, text: "Черновик");
        var conflict = await service.MutateAsync(session.AccessToken, conflictRequest, Ct);
        Assert.Equal("revision_conflict", conflict.Code);
        Assert.Equal(applied.ServerRecord, conflict.ServerRecord);
        await service.MutateAsync(session.AccessToken, Put(metadata, request.EntityId, 1, "Изменено"), Ct);
        Assert.Equal(SyncJson.Serialize(applied), SyncJson.Serialize(await service.MutateAsync(session.AccessToken, request, Ct)));
        Assert.Equal(SyncJson.Serialize(conflict), SyncJson.Serialize(await service.MutateAsync(session.AccessToken, conflictRequest, Ct)));
        var reused = await service.MutateAsync(session.AccessToken, Put(metadata, request.EntityId, 2, "Другое", request.OpId), Ct);
        Assert.Equal("op_id_reused", reused.Code);
        Assert.Equal(3, await Count(db, "sync_receipts"));
        Assert.Equal(2, await Count(db, "sync_changes"));
        var body = await db.Accounts.ScalarAsync<byte[]>($"SELECT body FROM {db.QuotedSchema}.sync_receipts WHERE op_id='{request.OpId}'");
        Assert.Equal(SyncJson.Serialize(applied), body);
        Assert.Equal(200, await db.Accounts.ScalarAsync<int>($"SELECT status FROM {db.QuotedSchema}.sync_receipts WHERE op_id='{request.OpId}'"));
        Assert.Equal(SyncJson.Digest(request), await db.Accounts.ScalarAsync<byte[]>($"SELECT request_digest FROM {db.QuotedSchema}.sync_receipts WHERE op_id='{request.OpId}'"));
        TestContext.Current.TestOutputHelper!.WriteLine("SQL receipt exact body/status/digest=true; immutable feed=2; receipts=3");
    }

    [Fact]
    public async Task Concurrent_create_and_update_have_single_winner_and_ordered_sequences()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var session = await Seed(accounts);
        var service = new SyncService(accounts, db.Configuration);
        var m = await service.MetadataAsync(session.AccessToken, Ct);
        Assert.NotNull(m);
        var id = Guid.NewGuid();
        var creates = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => service.MutateAsync(session.AccessToken, Put(m, id, text: $"{i}"), Ct)));
        Assert.Single(creates, x => x.Status == 200);
        var updates = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => service.MutateAsync(session.AccessToken, Put(m, id, 1, $"{i}"), Ct)));
        Assert.Single(updates, x => x.Status == 200);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => service.MutateAsync(session.AccessToken, Put(m, Guid.NewGuid()), Ct)));
        var page = Value(await service.ChangesAsync(session.AccessToken, m.SyncEpoch, 0, 200, Ct));
        Assert.Equal(Enumerable.Range(1, 14).Select(x => (long)x), page.Changes.Select(x => x.Sequence));
        Assert.Equal(14, page.Metadata.CurrentSequence);
        Assert.Equal(14, await Count(db, "sync_changes"));
    }

    [Theory]
    [InlineData("sync_records")]
    [InlineData("sync_changes")]
    [InlineData("sync_receipts")]
    public async Task Trigger_failure_rolls_back_record_feed_receipt_and_counter(string table)
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var session = await Seed(accounts);
        var service = new SyncService(accounts, db.Configuration);
        var m = await service.MetadataAsync(session.AccessToken, Ct);
        Assert.NotNull(m);
        await db.Accounts.ExecuteAsync($"""
            CREATE FUNCTION {db.QuotedSchema}.reject_owned() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic rejection'; END $$;
            CREATE TRIGGER reject_owned BEFORE INSERT ON {db.QuotedSchema}.{table}
            FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.reject_owned()
            """);
        var request = Put(m, Guid.NewGuid());
        await Failure(AccountFailure.DbUnavailable, () => service.MutateAsync(session.AccessToken, request, Ct));
        foreach (var name in new[] { "sync_records", "sync_changes", "sync_receipts" }) Assert.Equal(0, await Count(db, name));
        Assert.Equal(m, await service.MetadataAsync(session.AccessToken, Ct));
        await db.Accounts.ExecuteAsync($"DROP TRIGGER reject_owned ON {db.QuotedSchema}.{table}");
        Assert.Equal(1, (await service.MutateAsync(session.AccessToken, request, Ct)).Metadata.CurrentSequence);
    }
}
