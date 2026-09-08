using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using Zapara.Server.Accounts;

namespace Zapara.Server.Tests;

public sealed class AccountsConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = "accounts_test", ["Timetable:Schema"] = "timetable",
            ["ConnectionStrings:Timetable"] = "Host=localhost;Database=zapara_test;Password=DSN_CANARY;Include Error Detail=true;Log Parameters=true;Persist Security Info=true"
        };
        foreach (var (key, value) in overrides) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void DisabledByDefaultAndMigrationIsExplicit()
    {
        var config = Config(("Accounts:Enabled", null));
        Assert.False(AccountsConfiguration.IsEnabled(config));
        Assert.Throws<ArgumentException>(() => AccountsConfiguration.FromConfiguration(config));
        Assert.NotNull(AccountsConfiguration.FromConfiguration(config, forMigration: true));
    }

    [Theory]
    [InlineData("Accounts:Enabled", "perhaps")]
    [InlineData("Accounts:Enabled", "")]
    [InlineData("Accounts:Schema", "timetable")]
    [InlineData("Accounts:Schema", "bad\n")]
    [InlineData("Accounts:Schema", "Bad")]
    [InlineData("Accounts:Schema", "public")]
    [InlineData("Accounts:Schema", "pg_custom")]
    [InlineData("Accounts:Schema", "x; DROP SCHEMA public")]
    [InlineData("ConnectionStrings:Accounts", "")]
    [InlineData("ConnectionStrings:Accounts", "DSN_CANARY")]
    [InlineData("ConnectionStrings:AccountsMigration", "")]
    public void InvalidConfigurationDoesNotFallbackOrLeak(string key, string value)
    {
        var error = Assert.Throws<ArgumentException>(() => AccountsConfiguration.FromConfiguration(Config((key, value)), true));
        Assert.DoesNotContain("DSN_CANARY", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void WrapperUsesFallbackAndDisablesDriverSecrets()
    {
        var options = AccountsConfiguration.FromConfiguration(Config());
        using var ds = options.CreateDataSource();
        using var connection = ds.CreateConnection();
        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        Assert.True(parsed.Pooling);
        Assert.False(parsed.IncludeErrorDetail);
        Assert.False(parsed.LogParameters);
        Assert.False(parsed.PersistSecurityInfo);
        Assert.DoesNotContain("DSN_CANARY", options.ToString() + ds);
        using var migration = ds.CreateMigrationConnection();
        Assert.False(new NpgsqlConnectionStringBuilder(migration.ConnectionString).Pooling);
    }

    [Fact]
    public void ExplicitRuntimeAndOperatorOverridesAreIndependent()
    {
        var options = AccountsConfiguration.FromConfiguration(Config(
            ("ConnectionStrings:Accounts", "Host=remote;Database=runtime"),
            ("ConnectionStrings:AccountsMigration", "Host=operator;Database=migration")), true);
        using var ds = options.CreateDataSource();
        using var runtime = ds.CreateConnection();
        using var migration = ds.CreateMigrationConnection();
        Assert.Equal("runtime", runtime.Database);
        Assert.Equal("migration", migration.Database);
        Assert.Throws<ArgumentException>(() => AccountsConfiguration.FromConfiguration(Config(
            ("ConnectionStrings:Accounts", "Host=remote;Database=runtime")), true));
    }

    [Fact]
    public void RuntimeConfigurationCannotBypassRemoteMigrationOverride()
    {
        var options = AccountsConfiguration.FromConfiguration(Config(("ConnectionStrings:Accounts", "Host=remote;Database=runtime")));
        using var dataSource = options.CreateDataSource();
        Assert.Throws<InvalidOperationException>(() => dataSource.CreateMigrationConnection());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Host=remote;Port=56432;Database=zapara_test")]
    [InlineData("Host=localhost;Port=5432;Database=zapara_test")]
    [InlineData("Host=localhost;Port=56432;Database=production")]
    public void FixtureRejectsOtherTargets(string? dsn) => Assert.Throws<InvalidOperationException>(() => AccountsPostgresFixture.ValidateConnection(dsn));
}
