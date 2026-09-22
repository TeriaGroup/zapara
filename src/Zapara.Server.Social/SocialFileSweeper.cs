using Microsoft.Extensions.Hosting;
using Npgsql;
using Zapara.Server.Accounts;

namespace Zapara.Server.Social;

internal sealed class SocialFileSweeper(AccountsDataSource data, SocialConfiguration configuration, MediaStore media) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { }
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var connection = data.CreateConnection();
        await connection.OpenAsync(ct);
        await using (var probe = new NpgsqlCommand("SELECT to_regclass(@name)::text", connection))
        {
            probe.Parameters.AddWithValue("name", configuration.Schema + ".file_purge");
            if (await probe.ExecuteScalarAsync(ct) is null or DBNull or "") return;
        }
        while (!ct.IsCancellationRequested)
        {
            var names = new List<string>();
            await using (var command = new NpgsqlCommand($"SELECT stored_name FROM {configuration.QuotedSchema}.file_purge LIMIT 40", connection))
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
            if (names.Count == 0) return;
            var removed = 0;
            foreach (var name in names)
            {
                try { media.Delete(name); }
                catch (SocialException) { }
                catch (IOException) { continue; }
                await using var delete = new NpgsqlCommand($"DELETE FROM {configuration.QuotedSchema}.file_purge WHERE stored_name=@name", connection);
                delete.Parameters.AddWithValue("name", name);
                await delete.ExecuteNonQueryAsync(ct);
                removed++;
            }
            if (removed == 0) return;
        }
    }
}
