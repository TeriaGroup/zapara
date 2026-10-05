using System.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zapara.Server.Accounts;

/// <summary>Retries optional module preparation without preventing public HTTP startup.</summary>
public abstract class RetriedSchemaInitialization(TimeProvider clock, ILogger logger) : BackgroundService
{
    private volatile bool initialized;

    protected abstract Task PrepareAsync(CancellationToken ct);
    protected abstract Task<bool> VerifyAsync(CancellationToken ct);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await AttemptAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        if (!initialized) return false;
        try { return initialized = await VerifyAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { initialized = false; return false; }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(initialized ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(10), clock, stoppingToken);
                if (initialized)
                {
                    using var check = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    check.CancelAfter(TimeSpan.FromSeconds(5));
                    try { await IsReadyAsync(check.Token); }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { initialized = false; }
                }
                if (!initialized) await AttemptAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task AttemptAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await PrepareAsync(deadline.Token);
            initialized = await VerifyAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { initialized = false; }
        if (!initialized) logger.LogWarning("Подготовка данных модуля временно недоступна; попытка будет повторена.");
    }
}

public static class ModuleSchemaReadiness
{
    /// <summary>Checks required tables, upgraded columns and SELECT access without schema writes.</summary>
    public static async Task<bool> CheckAsync(AccountsDataSource data, string schema,
        IReadOnlyDictionary<string, string> projections, CancellationToken ct)
    {
        await using var connection = data.CreateConnection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using (var mode = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL search_path=pg_catalog", connection, transaction))
            await mode.ExecuteNonQueryAsync(ct);
        var quote = new NpgsqlCommandBuilder();
        foreach (var (table, columns) in projections)
        {
            await using var command = new NpgsqlCommand($"SELECT {columns} FROM {quote.QuoteIdentifier(schema)}.{quote.QuoteIdentifier(table)} LIMIT 0", connection, transaction);
            await using var reader = await command.ExecuteReaderAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return true;
    }
}
