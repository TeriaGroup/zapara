using Microsoft.Extensions.Configuration;
using Zapara.Server.Timetable;
using Xunit;
using Npgsql;

namespace Zapara.Server.Tests;

public class ConfigurationTests
{
    private const string Dummy = "Host=127.0.0.1;Database=foundation_test;Username=test;Password=dummy;Include Error Detail=true;Log Parameters=true;Persist Security Info=true;Pooling=false";

    internal static IConfiguration Config(string? schema = "tt_test", string? connection = Dummy) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Timetable"] = connection,
            ["Timetable:Schema"] = schema
        }).Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a\";drop schema public;")]
    [InlineData("a\n")]
    [InlineData("1bad")]
    public void Invalid_schema_is_rejected(string? schema) =>
        Assert.Throws<ArgumentException>(() => TimetableConfiguration.FromConfiguration(Config(schema)));

    [Fact]
    public void Schema_boundaries_and_quoting()
    {
        var name = "a" + new string('_', 62);
        var configuration = TimetableConfiguration.FromConfiguration(Config(name));
        Assert.Equal(name, configuration.Schema);
        Assert.Equal('"' + name + '"', configuration.QuotedSchema);
        Assert.Throws<ArgumentException>(() => TimetableConfiguration.FromConfiguration(Config(name + "a")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not a connection string")]
    public void Bad_connection_is_rejected_without_raw_details(string? value)
    {
        var error = Assert.Throws<ArgumentException>(() => TimetableConfiguration.FromConfiguration(Config(connection: value)));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("not a connection string", error.Message);
    }

    [Fact]
    public void Connections_are_safe_unopened_and_separate()
    {
        var configuration = TimetableConfiguration.FromConfiguration(Config());
        using var pool = configuration.CreateDataSource();
        using var read = pool.CreateConnection();
        using var first = configuration.CreateDedicatedConnection();
        using var second = configuration.CreateDedicatedConnection();
        Assert.NotSame(first, second);
        Assert.Equal(System.Data.ConnectionState.Closed, first.State);
        var pooled = new NpgsqlConnectionStringBuilder(read.ConnectionString);
        var dedicated = new NpgsqlConnectionStringBuilder(first.ConnectionString);
        Assert.True(pooled.Pooling);
        Assert.False(dedicated.Pooling);
        foreach (var builder in new[] { pooled, dedicated })
        {
            Assert.False(builder.IncludeErrorDetail);
            Assert.False(builder.LogParameters);
            Assert.False(builder.PersistSecurityInfo);
        }
        dedicated.Pooling = true;
        Assert.Equal("dummy", dedicated.Password);
        // Npgsql data-source connections redact credentials even before opening.
        dedicated.Remove("Password");
        pooled.Remove("Password");
        Assert.Equal(pooled.ConnectionString, dedicated.ConnectionString);
    }

    [Fact]
    public void Missing_connection_string_is_rejected()
    {
        var configuration = new ConfigurationBuilder().Build();
        Assert.Throws<ArgumentException>(() => TimetableConfiguration.FromConfiguration(configuration));
    }

    [Fact]
    public void Cli_does_not_claim_success_before_ingest_exists()
    {
        Assert.Equal(2, Ingest.Program.Main([]));
    }
}
