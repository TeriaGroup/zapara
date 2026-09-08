using Npgsql;
using Xunit;
using Zapara.Server.Admin;

namespace Zapara.Server.Tests;

public sealed class AdminMigrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Baseline_rerun_and_accounts_communities_preserved()
    {
        await using var db = await AdminPostgresFixture.CreateAsync();
        var accountsBefore = await db.Accounts.FingerprintAsync();
        await Task.WhenAll(db.Migrations.EnsureAsync(Ct), db.Migrations.EnsureAsync(Ct));
        Assert.Equal(4L, await db.TableCountAsync());
        Assert.Equal(AdminMigrations.BaselineChecksum, await db.ScalarAsync<string>($"SELECT checksum FROM {db.QuotedSchema}.schema_migrations"));
        await db.Migrations.EnsureAsync(Ct);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.schema_migrations"));
        Assert.Equal(accountsBefore, await db.Accounts.FingerprintAsync());
        await using var connection = db.Accounts.DataSource.CreateConnection();
        await connection.OpenAsync(Ct);
        await using var tx = await connection.BeginTransactionAsync(Ct);
        var actual = await AdminSchemaShape.FingerprintAsync(connection, tx, db.Configuration, Ct);
        TestContext.Current.TestOutputHelper?.WriteLine($"ADM fingerprint={actual}");
        Assert.Equal(AdminSchemaShape.Expected, actual);
    }

    [Fact]
    public async Task Foreign_schema_rejected_without_mutation()
    {
        await using var db = await AdminPostgresFixture.CreateAsync();
        await db.ExecuteAsync($"CREATE TABLE {db.QuotedSchema}.foreign_data (id int); INSERT INTO {db.QuotedSchema}.foreign_data VALUES (17)");
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Migrations.EnsureAsync(Ct));
        Assert.Equal(1L, await db.TableCountAsync());
        Assert.Equal(17, await db.ScalarAsync<int>($"SELECT id FROM {db.QuotedSchema}.foreign_data"));
    }
}
