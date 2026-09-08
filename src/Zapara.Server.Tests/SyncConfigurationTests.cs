using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;

namespace Zapara.Server.Tests;

public sealed class SyncConfigurationTests
{
    private static Dictionary<string, string?> Values() => new()
    {
        ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = "accounts_test", ["Sync:Schema"] = "private_sync",
        ["Timetable:Schema"] = "timetable", ["ConnectionStrings:Accounts"] = "Host=127.0.0.1;Database=zapara_test"
    };
    [Fact]
    public void Disabled_by_default_and_migration_opt_in_does_not_bypass_validation()
    {
        var values = Values();
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.False(SyncConfiguration.IsEnabled(config));
        Assert.Throws<ArgumentException>(() => SyncConfiguration.FromConfiguration(config));
        Assert.Equal("private_sync", SyncConfiguration.FromConfiguration(config, true).Schema);
        values["Accounts:Enabled"] = "false";
        config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.Equal("private_sync", SyncConfiguration.FromConfiguration(config, true).Schema);
        values["Sync:Enabled"] = "true";
        config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.Throws<ArgumentException>(() => SyncConfiguration.FromConfiguration(config, true));
        values["Sync:Enabled"] = "false";
        values["ConnectionStrings:Accounts"] = "CANARY_SECRET_INVALID";
        config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.DoesNotContain("CANARY", Assert.Throws<ArgumentException>(() => SyncConfiguration.FromConfiguration(config, true)).Message);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("pg_bad")]
    [InlineData("information_schema")]
    [InlineData("accounts_test")]
    [InlineData("timetable")]
    [InlineData("bad; DROP SCHEMA public")]
    public void Reserved_shared_or_unsafe_schema_rejected(string schema)
    {
        var values = Values();
        values["Sync:Schema"] = schema;
        Assert.Throws<ArgumentException>(() => SyncConfiguration.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), true));
    }

    [Fact]
    public async Task Separate_migration_database_rejected_before_open_or_write()
    {
        await using var db = await SyncPostgresFixture.CreateAsync();
        var raw = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")!;
        var values = Values();
        values["Accounts:Schema"] = db.Accounts.Schema;
        values["ConnectionStrings:Accounts"] = raw;
        values["ConnectionStrings:AccountsMigration"] = new NpgsqlConnectionStringBuilder(raw) { Database = "sync_test_nonexistent" }.ConnectionString;
        var accounts = AccountsConfiguration.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), true);
        await using var source = accounts.CreateDataSource();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SyncMigrations(source, db.Configuration).EnsureAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}'"));
    }
}
