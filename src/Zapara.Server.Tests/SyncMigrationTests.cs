using Xunit;
using Zapara.Server.Sync;

namespace Zapara.Server.Tests;

public sealed partial class SyncMigrationTests
{
    [Fact]
    public async Task Baseline_rerun_concurrent_and_accounts_preserved()
    {
        await using var db = await SyncPostgresFixture.CreateAsync();
        var before = await db.Accounts.FingerprintAsync();
        await Task.WhenAll(db.Migrations.EnsureAsync(TestContext.Current.CancellationToken), db.Migrations.EnsureAsync(TestContext.Current.CancellationToken));
        Assert.Equal(7L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}'"));
        Assert.Equal(SyncMigrations.BaselineChecksum, await db.Accounts.ScalarAsync<string>($"SELECT checksum FROM {db.QuotedSchema}.schema_migrations"));
        await db.Migrations.EnsureAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.schema_migrations"));
        Assert.Equal(before, await db.Accounts.FingerprintAsync());
        await db.Accounts.Migrations.EnsureAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Foreign_schema_rejected_without_mutation()
    {
        await using var db = await SyncPostgresFixture.CreateAsync();
        await db.Accounts.ExecuteAsync($"CREATE TABLE {db.QuotedSchema}.foreign_data (id int); INSERT INTO {db.QuotedSchema}.foreign_data VALUES (17)");
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Migrations.EnsureAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}'"));
        Assert.Equal(17, await db.Accounts.ScalarAsync<int>($"SELECT id FROM {db.QuotedSchema}.foreign_data"));
    }
}
