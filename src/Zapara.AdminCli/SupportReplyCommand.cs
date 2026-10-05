using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Zapara.Server.Accounts;
using Zapara.Server.Operator;
using Zapara.Server.Storage;

namespace Zapara.AdminCli;

internal static class SupportReplyCommand
{
    public static async Task<int> RunAsync(string thread, string bodyFile, TextWriter output, CancellationToken ct)
    {
        try
        {
            if (!Guid.TryParse(thread, out var id) || id == Guid.Empty || !File.Exists(bodyFile))
            {
                await output.WriteLineAsync("{\"operation\":\"support.reply\",\"status\":\"invalid\"}");
                return 2;
            }
            var body = await File.ReadAllTextAsync(bodyFile, ct);
            using var configuration = new ConfigurationManager();
            configuration.AddEnvironmentVariables();
            var options = AccountsConfiguration.FromConfiguration(configuration);
            await using var data = options.CreateDataSource();
            var objects = new RoutingObjectStore(configuration);
            var store = new SupportStore(data, configuration, objects);
            await store.ReplyAsync(id, body, ct);
            await output.WriteLineAsync(JsonSerializer.Serialize(new { operation = "support.reply", status = "committed" }));
            return 0;
        }
        catch (Exception)
        {
            await output.WriteLineAsync("{\"operation\":\"support.reply\",\"status\":\"failed\"}");
            return 5;
        }
    }
}
