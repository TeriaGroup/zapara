using Npgsql;
using Xunit;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class SyncBridgeTests
{
    [Theory]
    [InlineData("commit")]
    [InlineData("exception")]
    [InlineData("cancel")]
    public async Task Actor_transaction_rollback_and_context_lifetime(string outcome)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var schema = "sync_test_" + Guid.NewGuid().ToString("N");
        await db.ExecuteAsync($"CREATE SCHEMA {schema}; CREATE TABLE {schema}.probe (actor uuid NOT NULL)");
        try
        {
            var clock = new AccountClock();
            var service = new AccountService(db.DataSource, db.Configuration, clock);
            var session = await Seed(service);
            TrustedAccountContext? retained = null;
            using var cancellation = new CancellationTokenSource();
            async Task<int> Run() => await ((IAccountUnitOfWork)service).ExecuteAsync(session.AccessToken, async (context, ct) =>
            {
                retained = context;
                Assert.Equal(session.User.UserId, context.UserId);
                Assert.Equal(session.FamilyId, context.FamilyId);
                Assert.Equal(clock.Now, context.UtcNow);
                Assert.Same(context.Connection, context.Transaction.Connection);
                await using var command = new NpgsqlCommand($"INSERT INTO {schema}.probe VALUES (@actor)", context.Connection, context.Transaction);
                command.Parameters.AddWithValue("actor", context.UserId);
                await command.ExecuteNonQueryAsync(ct);
                Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {schema}.probe"));
                if (outcome == "exception") throw new InvalidOperationException("Synthetic callback failure");
                if (outcome == "cancel") cancellation.Cancel();
                return 19;
            }, cancellation.Token);
            if (outcome == "commit") Assert.Equal(19, await Run());
            else if (outcome == "exception") await Assert.ThrowsAsync<InvalidOperationException>(Run);
            else await Assert.ThrowsAnyAsync<OperationCanceledException>(Run);
            Assert.NotNull(retained);
            Assert.Throws<ObjectDisposedException>(() => retained.UserId);
            Assert.Throws<ObjectDisposedException>(() => retained.FamilyId);
            Assert.Throws<ObjectDisposedException>(() => retained.UtcNow);
            Assert.Throws<ObjectDisposedException>(() => retained.Connection);
            Assert.Throws<ObjectDisposedException>(() => retained.Transaction);
            Assert.Equal(outcome == "commit" ? 1L : 0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {schema}.probe"));
        }
        finally
        {
            await db.ExecuteAsync($"DROP SCHEMA {schema} CASCADE");
            Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{schema}'"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revoked_or_expired_access_never_calls_trusted_operation(bool expire)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var clock = new AccountClock();
        var service = new AccountService(db.DataSource, db.Configuration, clock);
        var session = await Seed(service);
        if (expire) clock.Now = session.AccessExpiresAt;
        else await service.LogoutAsync(session.AccessToken, TestContext.Current.CancellationToken);
        var called = false;
        await Failure(AccountFailure.InvalidSession, () => service.ExecuteAsync(session.AccessToken, (_, _) =>
        {
            called = true;
            return Task.FromResult(1);
        }, TestContext.Current.CancellationToken));
        Assert.False(called);
    }
}
