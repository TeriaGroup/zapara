using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Zapara.Server.Accounts;

namespace Zapara.Server.Sync;

public sealed class SyncMigrations(AccountsDataSource dataSource, SyncConfiguration configuration)
{
    public static string BaselineChecksum => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Baseline()))).ToLowerInvariant();
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        await using var connection = dataSource.CreateMigrationConnection();
        using (var runtime = dataSource.CreateConnection())
        {
            var a = new NpgsqlConnectionStringBuilder(runtime.ConnectionString);
            var b = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
            if (a.Host != b.Host || a.Port != b.Port || a.Database != b.Database) throw Invalid();
        }
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await Execute("SET LOCAL search_path = pg_catalog; SET LOCAL TIME ZONE 'UTC'", connection, tx, ct);
        var key = SHA256.HashData(Encoding.UTF8.GetBytes($"zapara.sync.migrations\n{connection.Database}\n{configuration.Schema}"));
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, tx))
        {
            advisory.Parameters.AddWithValue("key", BinaryPrimitives.ReadInt64BigEndian(key));
            await advisory.ExecuteNonQueryAsync(ct);
        }
        await using (var exists = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", connection, tx))
        {
            exists.Parameters.AddWithValue("schema", configuration.Schema);
            if ((long)(await exists.ExecuteScalarAsync(ct))! != 1) throw Invalid();
        }
        // Never initializes or repairs Accounts. Missing/unknown Accounts baseline fails before Sync DDL.
        await VerifyAccounts(connection, tx, ct);
        long objects;
        await using (var count = new NpgsqlCommand("""
            SELECT count(*) FROM pg_depend d JOIN pg_namespace n ON n.oid=d.refobjid
            WHERE d.refclassid='pg_namespace'::regclass AND n.nspname=@schema
            """, connection, tx))
        {
            count.Parameters.AddWithValue("schema", configuration.Schema);
            objects = (long)(await count.ExecuteScalarAsync(ct))!;
        }
        if (objects == 0)
        {
            await Execute(Baseline().Replace("__SYNC__", configuration.QuotedSchema, StringComparison.Ordinal)
                .Replace("__ACCOUNTS__", configuration.Accounts.QuotedSchema, StringComparison.Ordinal), connection, tx, ct);
            await using var history = new NpgsqlCommand($"INSERT INTO {configuration.QuotedSchema}.schema_migrations VALUES (1,@checksum,CURRENT_TIMESTAMP)", connection, tx);
            history.Parameters.AddWithValue("checksum", BaselineChecksum);
            await history.ExecuteNonQueryAsync(ct);
        }
        await SyncSchemaShape.VerifyAsync(connection, tx, configuration, ct);
        await using (var history = new NpgsqlCommand($"SELECT version,checksum FROM {configuration.QuotedSchema}.schema_migrations ORDER BY version", connection, tx))
        {
            await using var reader = await history.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.GetInt32(0) != 1 || reader.GetString(1) != BaselineChecksum || await reader.ReadAsync(ct)) throw Invalid();
        }
        ct.ThrowIfCancellationRequested();
        await tx.CommitAsync(ct);
    }
    private async Task VerifyAccounts(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
        => await AccountsMigrations.VerifyPreparedSchemaAsync(connection, tx, configuration.Accounts.Schema, ct);
    private static async Task Execute(string sql, NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        await command.ExecuteNonQueryAsync(ct);
    }
    private static string Baseline()
    {
        using var stream = typeof(SyncMigrations).Assembly.GetManifestResourceStream("Zapara.Server.Sync.Sql.001_sync.sql")
            ?? throw Invalid();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
    private static InvalidOperationException Invalid() => new("Схема или подключение Sync не соответствует известной миграции.");
}
