using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Zapara.Server.Sync;

namespace Zapara.AdminCli;

internal static class SyncMigrationCommand
{
    internal static async Task<int> RunAsync(TextWriter output, CancellationToken ct)
    {
        try
        {
            using var configuration = new ConfigurationManager();
            configuration.AddEnvironmentVariables();
            var options = SyncConfiguration.FromConfiguration(configuration, forMigration: true);
            await using var source = options.Accounts.CreateDataSource();
            await new SyncMigrations(source, options).EnsureAsync(ct);
            await output.WriteLineAsync(JsonSerializer.Serialize(new { operation = "sync.db-migrate", status = "committed", schema = options.Schema }));
            return 0;
        }
        catch (Exception)
        {
            await output.WriteLineAsync("{\"operation\":\"sync.db-migrate\",\"status\":\"failed\"}");
            return 5;
        }
    }
}
