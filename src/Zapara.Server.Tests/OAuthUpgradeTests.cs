using Xunit;
using Zapara.Contracts.Sync;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class OAuthUpgradeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Password_accounts_and_readonly_verification_work_on_supported_versions(int version)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true, version);
        var accounts = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var session = await Seed(accounts);
        Assert.Equal(new[] { "password" }, (await accounts.GetMeAsync(session.AccessToken, Ct)).AuthenticationMethods);
        await using (var connection = db.DataSource.CreateConnection())
        {
            await connection.OpenAsync(Ct);
            await using var tx = await connection.BeginTransactionAsync(Ct);
            await using var readOnly = new Npgsql.NpgsqlCommand("SET TRANSACTION READ ONLY", connection, tx);
            await readOnly.ExecuteNonQueryAsync(Ct);
            await AccountsMigrations.VerifyPreparedSchemaAsync(connection, tx, db.Schema, Ct);
        }
        await accounts.ChangePasswordAsync(session.AccessToken, new(Password, NewPassword), Ct);
        Assert.Equal(session.User.UserId, (await accounts.LoginAsync(Login(password: NewPassword), Ct)).User.UserId);
    }

    [Fact]
    public async Task Upgrade_preserves_password_sessions_and_existing_sync_data_then_sync_can_mutate()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true, targetVersion: 1);
        var accounts = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var session = await Seed(accounts);
        var syncSchema = "oauth_sync_" + Guid.NewGuid().ToString("N");
        var config = SyncPostgresFixture.Options(syncSchema, db.Schema);
        await db.ExecuteAsync($"CREATE SCHEMA \"{syncSchema}\"");
        try
        {
            var migrations = new SyncMigrations(db.DataSource, config);
            await migrations.EnsureAsync(Ct);
            var service = new SyncService(accounts, config);
            var metadata = await service.MetadataAsync(session.AccessToken, Ct);
            var request = new SyncMutation(metadata.SyncEpoch, Guid.NewGuid(), "homework", Guid.NewGuid(), 0, "upsert",
                new HomeworkValue("Математика", "математика", "Синтетическое задание", 2, new AccountClock().Now, new DateOnly(2026, 9, 7)));
            Assert.Equal(200, (await service.MutateAsync(session.AccessToken, request, Ct)).Status);
            var before = await SyncMigrationTests.State(db, syncSchema);
            await db.Migrations.EnsureAsync(Ct);
            await migrations.EnsureAsync(Ct);
            Assert.Equal(before, await SyncMigrationTests.State(db, syncSchema));
            Assert.Equal(session.User.UserId, (await accounts.AuthenticateAsync(session.AccessToken, Ct)).User.UserId);
            Assert.Equal(200, (await service.MutateAsync(session.AccessToken, request, Ct)).Status);
            Assert.Equal(1, (await service.MetadataAsync(session.AccessToken, Ct)).CurrentSequence);
            Assert.Equal(4, await db.ScalarAsync<int>($"SELECT max(version) FROM {db.QuotedSchema}.schema_migrations"));
        }
        finally
        {
            await db.ExecuteAsync($"DROP SCHEMA \"{syncSchema}\" CASCADE");
            Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{syncSchema}'"));
        }
    }

    [Theory]
    [InlineData("DROP INDEX __SCHEMA__.reauth_expiry")]
    [InlineData("ALTER TABLE __SCHEMA__.oauth_transactions DROP CONSTRAINT oauth_transactions_native_challenge_check")]
    [InlineData("DELETE FROM __SCHEMA__.schema_migrations WHERE version=2")]
    [InlineData("UPDATE __SCHEMA__.schema_migrations SET checksum='corrupted' WHERE version=2")]
    [InlineData("INSERT INTO __SCHEMA__.schema_migrations VALUES(5,'future',CURRENT_TIMESTAMP)")]
    public async Task Latest_schema_drift_and_history_are_rejected_without_repair(string sql)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        await db.ExecuteAsync(sql.Replace("__SCHEMA__", db.QuotedSchema));
        var before = await SyncMigrationTests.State(db, db.Schema);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Migrations.EnsureAsync(Ct));
        Assert.Equal(before, await SyncMigrationTests.State(db, db.Schema));
    }
}
