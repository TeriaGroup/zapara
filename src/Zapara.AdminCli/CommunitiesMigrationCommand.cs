using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Zapara.Server.Communities;

namespace Zapara.AdminCli;

internal static class CommunitiesMigrationCommand
{
    internal static async Task<int> RunAsync(TextWriter output, CancellationToken ct)
    {
        try
        {
            using var configuration = new ConfigurationManager();
            configuration.AddEnvironmentVariables();
            var options = CommunitiesConfiguration.FromConfiguration(configuration, forMigration: true);
            await using var source = options.Accounts.CreateDataSource();
            await new CommunitiesMigrations(source, options).EnsureAsync(ct);
            await output.WriteLineAsync(JsonSerializer.Serialize(new { operation = "communities.db-migrate", status = "committed", schema = options.Schema }));
            return 0;
        }
        catch (Exception)
        {
            await output.WriteLineAsync("{\"operation\":\"communities.db-migrate\",\"status\":\"failed\"}");
            return 5;
        }
    }
}
