using Xunit;
using Zapara.Server.Communities;

namespace Zapara.Server.Tests;

public sealed partial class CommunityMigrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Baseline_rerun_concurrent_and_accounts_preserved()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync();
        var before = await db.Accounts.FingerprintAsync();
        await Task.WhenAll(db.Migrations.EnsureAsync(Ct), db.Migrations.EnsureAsync(Ct));
        Assert.Equal(13L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}'"));
        Assert.Equal(CommunitiesMigrations.BaselineChecksum, await db.Accounts.ScalarAsync<string>($"SELECT checksum FROM {db.QuotedSchema}.schema_migrations"));
        await db.Migrations.EnsureAsync(Ct);
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.schema_migrations"));
        Assert.Equal(before, await db.Accounts.FingerprintAsync());
        Assert.Equal(2L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.Accounts.QuotedSchema}.schema_migrations"));
        await using var connection = db.Accounts.DataSource.CreateConnection();
        await connection.OpenAsync(Ct);
        await using var tx = await connection.BeginTransactionAsync(Ct);
        var actual = await CommunitiesSchemaShape.FingerprintAsync(connection, tx, db.Configuration, Ct);
        TestContext.Current.TestOutputHelper?.WriteLine($"COM fingerprint={actual}");
        Assert.Equal(CommunitiesSchemaShape.Expected, actual);
    }

    [Fact]
    public async Task Foreign_schema_rejected_without_mutation()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync();
        await db.Accounts.ExecuteAsync($"CREATE TABLE {db.QuotedSchema}.foreign_data (id int); INSERT INTO {db.QuotedSchema}.foreign_data VALUES (17)");
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Migrations.EnsureAsync(Ct));
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}'"));
        Assert.Equal(17, await db.Accounts.ScalarAsync<int>($"SELECT id FROM {db.QuotedSchema}.foreign_data"));
    }
}
