using Npgsql;
using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class SyncMigrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("checksum")]
    [InlineData("future")]
    [InlineData("historyMissing")]
    [InlineData("column")]
    [InlineData("check")]
    [InlineData("index")]
    [InlineData("cascade")]
    [InlineData("partial")]
    [InlineData("trigger")]
    public async Task Drift_and_unknown_history_never_repaired(string scenario)
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var s = db.QuotedSchema;
        await db.Accounts.ExecuteAsync(scenario switch
        {
            "checksum" => $"UPDATE {s}.schema_migrations SET checksum=repeat('0',64)",
            "future" => $"INSERT INTO {s}.schema_migrations VALUES(2,repeat('0',64),now())",
            "historyMissing" => $"DELETE FROM {s}.schema_migrations",
            "column" => $"ALTER TABLE {s}.sync_receipts ALTER COLUMN body DROP NOT NULL",
            "check" => $"ALTER TABLE {s}.sync_state DROP CONSTRAINT sync_state_sequence_check; ALTER TABLE {s}.sync_state ADD CONSTRAINT sync_state_sequence_check CHECK (sequence >= -1)",
            "index" => $"DROP INDEX {s}.sync_records_retention",
            "cascade" => $"ALTER TABLE {s}.sync_state DROP CONSTRAINT sync_state_user_id_fkey; ALTER TABLE {s}.sync_state ADD CONSTRAINT sync_state_user_id_fkey FOREIGN KEY(user_id) REFERENCES {db.Accounts.QuotedSchema}.users(user_id)",
            "partial" => $"DROP TABLE {s}.sync_manifest_items",
            "trigger" => $"CREATE FUNCTION {s}.unexpected() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END $$; CREATE TRIGGER unexpected BEFORE INSERT ON {s}.sync_records FOR EACH ROW EXECUTE FUNCTION {s}.unexpected()",
            _ => throw new InvalidOperationException()
        });
        var before = await State(db.Accounts, db.Schema);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Migrations.EnsureAsync(Ct));
        Assert.Equal(before, await State(db.Accounts, db.Schema));
        Console.WriteLine($"SQL rejection={scenario} no_mutation=true");
    }

    [Fact]
    public async Task Missing_schema_not_created_and_wrong_accounts_target_never_rebound()
    {
        await using var db = await SyncPostgresFixture.CreateAsync();
        await db.Accounts.ExecuteAsync($"DROP SCHEMA {db.QuotedSchema}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Migrations.EnsureAsync(Ct));
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{db.Schema}'"));
        await db.Accounts.ExecuteAsync($"CREATE SCHEMA {db.QuotedSchema}");
        await db.Migrations.EnsureAsync(Ct);
        await using var other = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var before = await State(db.Accounts, db.Schema);
        var accountsBefore = await State(db.Accounts, db.Accounts.Schema);
        var otherBefore = await State(other, other.Schema);
        var mismatched = new SyncMigrations(db.Accounts.DataSource, SyncPostgresFixture.Options(db.Schema, other.Schema));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mismatched.EnsureAsync(Ct));
        Assert.Equal(before, await State(db.Accounts, db.Schema));
        Assert.Equal(accountsBefore, await State(db.Accounts, db.Accounts.Schema));
        Assert.Equal(otherBefore, await State(other, other.Schema));
    }

    [Fact]
    public async Task Unprepared_accounts_is_not_initialized_by_sync()
    {
        await using var db = await SyncPostgresFixture.CreateAsync();
        await using var emptyAccounts = await AccountsPostgresFixture.CreateAsync(Console.WriteLine);
        var migration = new SyncMigrations(db.Accounts.DataSource, SyncPostgresFixture.Options(db.Schema, emptyAccounts.Schema));
        var before = await State(db.Accounts, db.Schema);
        await Assert.ThrowsAsync<InvalidOperationException>(() => migration.EnsureAsync(Ct));
        Assert.Equal(before, await State(db.Accounts, db.Schema));
        Assert.Equal(0L, await emptyAccounts.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{emptyAccounts.Schema}'"));
    }

    [Fact]
    public async Task Altered_accounts_shape_is_rejected_before_sync_schema_mutation()
    {
        await using var db = await SyncPostgresFixture.CreateAsync();
        await db.Accounts.ExecuteAsync($"""
            ALTER TABLE {db.Accounts.QuotedSchema}.password_credentials
            DROP CONSTRAINT password_credentials_user_id_fkey;
            ALTER TABLE {db.Accounts.QuotedSchema}.password_credentials
            ADD CONSTRAINT password_credentials_user_id_fkey
            FOREIGN KEY(user_id) REFERENCES {db.Accounts.QuotedSchema}.users(user_id) ON DELETE CASCADE;
            """);
        var accountsBefore = await State(db.Accounts, db.Accounts.Schema);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Migrations.EnsureAsync(Ct));
        Assert.Equal(accountsBefore, await State(db.Accounts, db.Accounts.Schema));
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}'"));
    }

    [Fact]
    public async Task Migration_preserves_nonempty_accounts_and_timetable()
    {
        await using var timetable = await PostgresFixture.CreateAsync(Console.WriteLine, ct: Ct);
        await using (var lease = await timetable.Store.TryAcquireAsync(Ct))
        {
            Assert.NotNull(lease);
            await timetable.Store.PublishAsync(lease, TestSnapshotFactory.Create(timetable.Clock), Ct);
        }
        await using var db = await SyncPostgresFixture.CreateAsync();
        await Seed(new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock()));
        var accounts = await State(db.Accounts, db.Accounts.Schema);
        var timetableBefore = await State(db.Accounts, timetable.Schema);
        await db.Migrations.EnsureAsync(Ct);
        await db.Migrations.EnsureAsync(Ct);
        Assert.Equal(accounts, await State(db.Accounts, db.Accounts.Schema));
        Assert.Equal(timetableBefore, await State(db.Accounts, timetable.Schema));
        Assert.Equal("aa5f311445805d2e8ca0f0c6c2b06ff6cbf37041f0026a44541a96657e50e50b", AccountsMigrations.BaselineChecksum);
        await db.Accounts.Migrations.EnsureAsync(Ct);
        await timetable.Store.EnsureSchemaAsync(Ct);
    }

    internal static async Task<string> State(AccountsPostgresFixture db, string schema)
    {
        await using var connection = db.DataSource.CreateConnection();
        await connection.OpenAsync(Ct);
        await using var tx = await connection.BeginTransactionAsync(Ct);
        var shape = await AccountsSchemaShape.FingerprintAsync(connection, tx, schema, Ct);
        var tables = new List<string>();
        await using (var command = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname=@schema ORDER BY tablename", connection, tx))
        {
            command.Parameters.AddWithValue("schema", schema);
            await using var reader = await command.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct)) tables.Add(reader.GetString(0));
        }
        foreach (var table in tables)
        {
            var quoted = new NpgsqlCommandBuilder().QuoteIdentifier(table);
            await using var command = new NpgsqlCommand($"SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text),'[]')::text FROM \"{schema}\".{quoted} t", connection, tx);
            shape += (string)(await command.ExecuteScalarAsync(Ct))!;
        }
        return shape;
    }
}
