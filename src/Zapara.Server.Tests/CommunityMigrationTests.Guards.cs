using Npgsql;
using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Communities;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class CommunityMigrationTests
{
    [Theory]
    [InlineData("checksum")]
    [InlineData("future")]
    [InlineData("historyMissing")]
    [InlineData("column")]
    [InlineData("index")]
    [InlineData("partial")]
    [InlineData("trigger")]
    public async Task Drift_and_unknown_history_never_repaired(string scenario)
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        var s = db.QuotedSchema;
        await db.Accounts.ExecuteAsync(scenario switch
        {
            "checksum" => $"UPDATE {s}.schema_migrations SET checksum=repeat('0',64)",
            "future" => $"INSERT INTO {s}.schema_migrations VALUES(2,repeat('0',64),now())",
            "historyMissing" => $"DELETE FROM {s}.schema_migrations",
            "column" => $"ALTER TABLE {s}.communities ALTER COLUMN name DROP NOT NULL",
            "index" => $"DROP INDEX {s}.join_requests_pending",
            "partial" => $"DROP TABLE {s}.votes",
            "trigger" => $"DROP TRIGGER community_audit_no_update ON {s}.community_audit",
            _ => throw new InvalidOperationException()
        });
        var before = await State(db.Accounts, db.Schema);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Migrations.EnsureAsync(Ct));
        Assert.Equal(before, await State(db.Accounts, db.Schema));
        Console.WriteLine($"SQL rejection={scenario} no_mutation=true");
    }

    [Fact]
    public async Task Missing_schema_not_created_and_unprepared_accounts_is_not_initialized()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync();
        await db.Accounts.ExecuteAsync($"DROP SCHEMA {db.QuotedSchema}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Migrations.EnsureAsync(Ct));
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{db.Schema}'"));
        await db.Accounts.ExecuteAsync($"CREATE SCHEMA {db.QuotedSchema}");
        await using var emptyAccounts = await AccountsPostgresFixture.CreateAsync(Console.WriteLine);
        var migration = new CommunitiesMigrations(db.Accounts.DataSource, CommunityPostgresFixture.Options(db.Schema, emptyAccounts.Schema));
        var before = await State(db.Accounts, db.Schema);
        await Assert.ThrowsAsync<InvalidOperationException>(() => migration.EnsureAsync(Ct));
        Assert.Equal(before, await State(db.Accounts, db.Schema));
        Assert.Equal(0L, await emptyAccounts.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{emptyAccounts.Schema}'"));
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
