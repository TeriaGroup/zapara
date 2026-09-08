using Npgsql;
using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class SyncDomainTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queued_reads_and_receipt_retry_reenter_authorization_before_callback(bool expire)
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var clock = new AccountClock();
        var accounts = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, clock);
        var session = await Seed(accounts);
        var observed = new ObservedUnitOfWork(accounts);
        var service = new SyncService(observed, db.Configuration);
        var m = await service.MetadataAsync(session.AccessToken, Ct);
        Assert.NotNull(m);
        var request = Put(m, Guid.NewGuid());
        await service.MutateAsync(session.AccessToken, request, Ct);
        var manifest = await service.BeginResyncAsync(session.AccessToken, Ct);
        var callbacks = observed.Callbacks;
        await using var connection = db.Accounts.DataSource.CreateConnection();
        await connection.OpenAsync(Ct);
        await using var tx = await connection.BeginTransactionAsync(Ct);
        await using (var cmd = new NpgsqlCommand($"SELECT user_id FROM {db.Accounts.QuotedSchema}.users WHERE user_id=@u FOR UPDATE", connection, tx))
        {
            cmd.Parameters.AddWithValue("u", session.User.UserId);
            await cmd.ExecuteScalarAsync(Ct);
        }
        var queued = new Task[]
        {
            service.MetadataAsync(session.AccessToken, Ct), service.MutateAsync(session.AccessToken, request, Ct),
            service.ChangesAsync(session.AccessToken, m.SyncEpoch, 0, 100, Ct),
            service.BeginResyncAsync(session.AccessToken, Ct), service.ReadResyncPageAsync(session.AccessToken, manifest.ManifestId, 0, 100, Ct)
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_stat_activity WHERE wait_event_type='Lock' AND query LIKE '%{db.Accounts.Schema}%' AND pid<>pg_backend_pid()") < queued.Length)
            await Task.Delay(20, timeout.Token);
        if (expire) clock.Now = session.AccessExpiresAt;
        else
        {
            // Fixture-owned equivalent of committed revocation, respecting user -> family lock order.
            await using var cmd = new NpgsqlCommand($"UPDATE {db.Accounts.QuotedSchema}.session_families SET revoked_at=@now WHERE family_id=@f", connection, tx);
            cmd.Parameters.AddWithValue("now", clock.Now);
            cmd.Parameters.AddWithValue("f", session.FamilyId);
            await cmd.ExecuteNonQueryAsync(Ct);
        }
        await tx.CommitAsync(Ct);
        foreach (var task in queued) await Failure(AccountFailure.InvalidSession, () => task);
        Assert.Equal(callbacks, observed.Callbacks);
        Assert.Equal(1, await Count(db, "sync_receipts"));
        Assert.Equal(1, await Count(db, "sync_changes"));
    }

    private sealed class ObservedUnitOfWork(IAccountUnitOfWork inner) : IAccountUnitOfWork
    {
        private int callbacks;
        public int Callbacks => callbacks;
        public Task<T> ExecuteAsync<T>(string accessToken, Func<TrustedAccountContext, CancellationToken, Task<T>> operation, CancellationToken ct = default)
            => inner.ExecuteAsync(accessToken, (context, token) =>
            {
                Interlocked.Increment(ref callbacks);
                return operation(context, token);
            }, ct);
    }
}
