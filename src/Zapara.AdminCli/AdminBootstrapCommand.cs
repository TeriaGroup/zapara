using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Zapara.Server.Admin;

namespace Zapara.AdminCli;

internal static class AdminBootstrapCommand
{
    internal static async Task<int> RunAsync(string userIdRaw, TextWriter output, CancellationToken ct)
    {
        if (!Guid.TryParseExact(userIdRaw, "D", out var userId) || userId == Guid.Empty)
        {
            await output.WriteLineAsync("{\"operation\":\"arguments\",\"status\":\"invalid\"}");
            return 2;
        }
        try
        {
            using var configuration = new ConfigurationManager();
            configuration.AddEnvironmentVariables();
            var options = AdminConfiguration.FromConfiguration(configuration, forMigration: true);
            await using var source = options.Accounts.CreateDataSource();
            await new AdminMigrations(source, options).EnsureAsync(ct);
            var result = await AdminBootstrap.RunAsync(source, options, userId, ct);
            if (result == AdminBootstrapOutcome.Committed)
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    operation = "admin.bootstrap",
                    status = "committed",
                    userId = userId.ToString("D")
                }));
                return 0;
            }
            if (result == AdminBootstrapOutcome.Refused)
            {
                await output.WriteLineAsync("{\"operation\":\"admin.bootstrap\",\"status\":\"refused\"}");
                return 4;
            }
            await output.WriteLineAsync("{\"operation\":\"admin.bootstrap\",\"status\":\"failed\"}");
            return 5;
        }
        catch (Exception)
        {
            await output.WriteLineAsync("{\"operation\":\"admin.bootstrap\",\"status\":\"failed\"}");
            return 5;
        }
    }
}
