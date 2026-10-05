using Microsoft.Extensions.Configuration;
using Xunit;
using Zapara.Server.Admin;

namespace Zapara.Server.Tests;

public sealed class AdminConfigurationTests
{
    private static Dictionary<string, string?> Values() => new()
    {
        ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = "accounts_test",
        ["Communities:Schema"] = "communities",
        ["Admin:Schema"] = "admin_mod", ["Timetable:Schema"] = "timetable",
        ["ConnectionStrings:Accounts"] = "Host=127.0.0.1;Database=zapara_test"
    };

    [Fact]
    public void Disabled_by_default_and_migration_opt_in_does_not_bypass_validation()
    {
        var values = Values();
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.False(AdminConfiguration.IsEnabled(config));
        Assert.Throws<ArgumentException>(() => AdminConfiguration.FromConfiguration(config));
        Assert.Equal("admin_mod", AdminConfiguration.FromConfiguration(config, true).Schema);
        values["Accounts:Enabled"] = "false";
        config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.Equal("admin_mod", AdminConfiguration.FromConfiguration(config, true).Schema);
        values["Admin:Enabled"] = "true";
        config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.Throws<ArgumentException>(() => AdminConfiguration.FromConfiguration(config, true));
        values["Accounts:Enabled"] = "true";
        values["Communities:Enabled"] = "false";
        values["Admin:Enabled"] = "true";
        config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.Throws<ArgumentException>(() => AdminConfiguration.FromConfiguration(config));
        values["Communities:Enabled"] = "true";
        values["Admin:Enabled"] = "false";
        values["ConnectionStrings:Accounts"] = "CANARY_SECRET_INVALID";
        config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.DoesNotContain("CANARY", Assert.Throws<ArgumentException>(() => AdminConfiguration.FromConfiguration(config, true)).Message);
        Assert.Equal("AdminConfiguration { [REDACTED] }", AdminConfiguration.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(Values()).Build(), true).ToString());
    }

    [Theory]
    [InlineData("public")]
    [InlineData("pg_bad")]
    [InlineData("information_schema")]
    [InlineData("accounts_test")]
    [InlineData("communities")]
    [InlineData("timetable")]
    [InlineData("bad; DROP SCHEMA public")]
    public void Reserved_shared_or_unsafe_schema_rejected(string schema)
    {
        var values = Values();
        values["Admin:Schema"] = schema;
        Assert.Throws<ArgumentException>(() => AdminConfiguration.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(), true));
    }
}
