using Xunit;

namespace Zapara.Server.Tests;

[CollectionDefinition("Environment", DisableParallelization = true)]
public class EnvironmentCollection;

[Collection("Environment")]
public class EntryConfigurationTests
{
    [Fact]
    public void Cli_uses_shared_environment_configuration()
    {
        const string schemaKey = "Timetable__Schema";
        const string connectionKey = "ConnectionStrings__Timetable";
        var previousSchema = Environment.GetEnvironmentVariable(schemaKey);
        var previousConnection = Environment.GetEnvironmentVariable(connectionKey);
        try
        {
            Environment.SetEnvironmentVariable(schemaKey, "tt_foundation");
            Environment.SetEnvironmentVariable(connectionKey, "Host=127.0.0.1;Database=test;Username=test;Password=dummy");
            Assert.Equal("tt_foundation", Ingest.Program.LoadConfiguration().Schema);
            Environment.SetEnvironmentVariable(schemaKey, "bad schema");
            Assert.Throws<ArgumentException>(() => Ingest.Program.LoadConfiguration());
        }
        finally
        {
            Environment.SetEnvironmentVariable(schemaKey, previousSchema);
            Environment.SetEnvironmentVariable(connectionKey, previousConnection);
        }
    }
}
