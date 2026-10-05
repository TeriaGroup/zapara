using Microsoft.Extensions.Configuration;
using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Social;

namespace Zapara.Server.Tests;

[Collection("Account runtime")]
public sealed class UploadQuotaRegressionTests
{
    [Fact]
    public async Task Failed_counter_write_rejects_upload_before_storing_bytes()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, initialize: true);
        var accounts = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var session = await AccountTestSupport.Seed(accounts);
        await db.ExecuteAsync($"CREATE TABLE {db.QuotedSchema}.quota_counters(scope text, scope_id text, bytes bigint CHECK(bytes <= 0), PRIMARY KEY(scope,scope_id))");
        var objects = new MemoryObjectStore();
        var upload = new StudentUpload(objects, Ledger(db));
        var error = await Assert.ThrowsAsync<SocialException>(() => upload.Accept(accounts, session.AccessToken, null,
            "test-upload.bin", [1, 2, 3], TestContext.Current.CancellationToken));
        Assert.Equal(503, error.Status);
        Assert.False(objects.Contains("test-upload.bin"));
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.quota_counters"));
    }

    [Fact]
    public async Task Cancelled_failed_upload_releases_reserved_bytes_and_preserves_storage_failure()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, initialize: true);
        var accounts = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var session = await AccountTestSupport.Seed(accounts);
        var ledger = Ledger(db);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var upload = new StudentUpload(new CancelledStore(cancellation), ledger);
        await Assert.ThrowsAsync<IOException>(() => upload.Accept(accounts, session.AccessToken, null,
            "test-upload.bin", [1, 2, 3], cancellation.Token));
        Assert.Equal(0, ledger.Usage(session.User.UserId, null).UserUsed);
    }

    [Fact]
    public async Task Concurrent_reservations_cannot_spend_the_same_remaining_quota()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, initialize: true);
        var accounts = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var session = await AccountTestSupport.Seed(accounts);
        await db.ExecuteAsync($"""
            CREATE TABLE {db.QuotedSchema}.system_settings(key text PRIMARY KEY, value text NOT NULL);
            INSERT INTO {db.QuotedSchema}.system_settings VALUES ('quota_user_bytes','10');
            CREATE TABLE {db.QuotedSchema}.quota_counters(scope text, scope_id text, bytes bigint, PRIMARY KEY(scope,scope_id));
            INSERT INTO {db.QuotedSchema}.quota_counters VALUES ('user','{session.User.UserId:D}',0);
            CREATE FUNCTION {db.QuotedSchema}.slow_counter() RETURNS trigger LANGUAGE plpgsql AS $slow$
              BEGIN PERFORM pg_sleep(0.15); RETURN NEW; END $slow$;
            CREATE TRIGGER slow_counter BEFORE UPDATE ON {db.QuotedSchema}.quota_counters
              FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.slow_counter();
            """);
        var ledger = Ledger(db);
        async Task<bool> Reserve()
        {
            try { await ledger.Reserve(accounts, session.AccessToken, 6, null, TestContext.Current.CancellationToken); return true; }
            catch (SocialException error) when (error.Code == "quota_user") { return false; }
        }
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Reserve()));
        Assert.Single(outcomes, value => value);
        Assert.Equal(6, ledger.Usage(session.User.UserId, null).UserUsed);
    }

    private static QuotaLedger Ledger(AccountsPostgresFixture db) => new(db.DataSource,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Operator:Schema"] = db.Schema }).Build());

    [Fact]
    public async Task Reservation_release_uses_original_group_after_revocation_and_is_idempotent()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, initialize: true);
        var accounts = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var session = await AccountTestSupport.Seed(accounts);
        var group = Guid.NewGuid();
        var otherGroup = Guid.NewGuid();
        await db.ExecuteAsync($"""
            CREATE TABLE {db.QuotedSchema}.memberships(user_id uuid,community_id uuid,status text,created_at timestamptz);
            CREATE TABLE {db.QuotedSchema}.catalog_maps(community_id uuid,group_id text);
            INSERT INTO {db.QuotedSchema}.memberships VALUES ('{session.User.UserId}','{group}','active',now());
            INSERT INTO {db.QuotedSchema}.catalog_maps VALUES ('{group}','original'),('{otherGroup}','changed');
            """);
        var ledger = new QuotaLedger(db.DataSource, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Operator:Schema"] = db.Schema, ["Communities:Schema"] = db.Schema }).Build());
        var upload = new StudentUpload(new MemoryObjectStore(), ledger);
        var original = await upload.AcceptTrackedAsync(accounts, session.AccessToken, null, "first-upload.bin", [1, 2, 3], TestContext.Current.CancellationToken);
        await upload.Accept(accounts, session.AccessToken, null, "other-upload.bin", [4, 5], TestContext.Current.CancellationToken);
        await db.ExecuteAsync($"UPDATE {db.QuotedSchema}.memberships SET community_id='{otherGroup}' WHERE user_id='{session.User.UserId}'");
        await accounts.LogoutAsync(session.AccessToken, TestContext.Current.CancellationToken);
        await original.Reservation.ReleaseAsync(TestContext.Current.CancellationToken);
        await original.Reservation.ReleaseAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2L, await db.ScalarAsync<long>($"SELECT bytes FROM {db.QuotedSchema}.quota_counters WHERE scope='user' AND scope_id='{session.User.UserId:D}'"));
        Assert.Equal(2L, await db.ScalarAsync<long>($"SELECT bytes FROM {db.QuotedSchema}.quota_counters WHERE scope='group' AND scope_id='original'"));
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.quota_counters WHERE scope='group' AND scope_id='changed'"));
    }

    private sealed class CancelledStore(CancellationTokenSource cancellation) : IObjectStore
    {
        public void Put(string key, byte[] bytes) { cancellation.Cancel(); throw new IOException("Synthetic storage failure"); }
        public byte[]? Get(string key) => null;
        public void Delete(string key) { }
    }
}
