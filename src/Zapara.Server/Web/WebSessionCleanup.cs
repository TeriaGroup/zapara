using Npgsql;
using Zapara.Server.Accounts;

namespace Zapara.Server.Web;

internal sealed class WebSessionCleanup(IServiceProvider services, ILogger<WebSessionCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var source = services.GetRequiredService<AccountsDataSource>();
                var schema = services.GetRequiredService<AccountsConfiguration>().QuotedSchema;
                var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
                await using var connection = source.CreateConnection();
                await connection.OpenAsync(stoppingToken);
                await using var command = new NpgsqlCommand($"""
                    DELETE FROM {schema}.web_sessions WHERE session_hash IN (
                        SELECT w.session_hash FROM {schema}.web_sessions w
                        JOIN {schema}.session_families f ON f.family_id=w.family_id
                        WHERE w.expires_at<=@now OR f.revoked_at IS NOT NULL OR f.expires_at<=@now
                        ORDER BY w.session_hash LIMIT 1024 FOR UPDATE OF w SKIP LOCKED);
                    DELETE FROM {schema}.web_oauth_flows WHERE transaction_id IN (
                        SELECT transaction_id FROM {schema}.web_oauth_flows WHERE expires_at<=@now
                        ORDER BY transaction_id LIMIT 1024 FOR UPDATE SKIP LOCKED)
                    """, connection);
                command.Parameters.AddWithValue("now", now);
                await command.ExecuteNonQueryAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) when (e is NpgsqlException or AccountServiceException or TimeoutException)
            { logger.LogWarning("Очистка браузерных сессий временно недоступна."); }
        }
    }
}
