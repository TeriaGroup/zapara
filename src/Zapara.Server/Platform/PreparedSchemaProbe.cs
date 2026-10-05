using System.Data;
using Npgsql;
using Zapara.Server.Accounts;

namespace Zapara.Server.Platform;

internal static class PreparedSchemaProbe
{
    internal static async Task<bool> ReadAsync(AccountsDataSource source, string schema,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> verify, CancellationToken ct)
    {
        await using var connection = source.CreateConnection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using (var mode = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL search_path=pg_catalog", connection, transaction))
            await mode.ExecuteNonQueryAsync(ct);
        await verify(connection, transaction, ct);
        var tables = new List<string>();
        await using (var names = new NpgsqlCommand("SELECT relname FROM pg_class WHERE relnamespace=@schema::regnamespace AND relkind IN ('r','p') ORDER BY relname", connection, transaction))
        {
            names.Parameters.AddWithValue("schema", schema);
            await using var reader = await names.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) tables.Add(reader.GetString(0));
        }
        if (tables.Count == 0) return false;
        var quote = new NpgsqlCommandBuilder();
        foreach (var table in tables)
        {
            // Checks runtime SELECT access to all required columns; no row values are logged or returned by health.
            await using var command = new NpgsqlCommand($"SELECT * FROM {quote.QuoteIdentifier(schema)}.{quote.QuoteIdentifier(table)} LIMIT 1", connection, transaction);
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return true;
    }
}
