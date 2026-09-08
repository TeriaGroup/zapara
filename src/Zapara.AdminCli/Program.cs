using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Zapara.Server.Accounts;

namespace Zapara.AdminCli;

public static class Program
{
    public static Task<int> Main(string[] args) => RunAsync(args, Console.Out);

    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken ct = default)
    {
        if (args.Length == 2 && args[0] == "sync" && args[1] == "db-migrate")
            return await SyncMigrationCommand.RunAsync(output, ct);
        if (args.Length == 2 && args[0] == "communities" && args[1] == "db-migrate")
            return await CommunitiesMigrationCommand.RunAsync(output, ct);
        if (args.Length == 4 && args[0] == "admin" && args[1] == "bootstrap" && args[2] == "--user-id")
            return await AdminBootstrapCommand.RunAsync(args[3], output, ct);
        if (args.Length != 2 || args[0] != "accounts" || args[1] != "db-migrate")
        {
            await output.WriteLineAsync("{\"operation\":\"arguments\",\"status\":\"invalid\"}");
            return 2;
        }
        try
        {
            using var configuration = new ConfigurationManager();
            configuration.AddEnvironmentVariables();
            var options = AccountsConfiguration.FromConfiguration(configuration, forMigration: true);
            await using var dataSource = options.CreateDataSource();
            await new AccountsMigrations(dataSource, options).EnsureAsync(ct);
            await output.WriteLineAsync(JsonSerializer.Serialize(new { operation = "accounts.db-migrate", status = "committed", schema = options.Schema }));
            return 0;
        }
        catch (Exception)
        {
            // Operator output must never contain driver exceptions or connection details.
            await output.WriteLineAsync("{\"operation\":\"accounts.db-migrate\",\"status\":\"failed\"}");
            return 5;
        }
    }
}
