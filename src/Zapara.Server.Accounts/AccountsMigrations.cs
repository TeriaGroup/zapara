using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Zapara.Server.Accounts;

public sealed class AccountsMigrations(AccountsDataSource dataSource, AccountsConfiguration configuration)
{
    public const int CurrentVersion = 6;

    /// <summary>Read-only runtime readiness: historical but valid migration targets are not the current platform.</summary>
    public static async Task VerifyCurrentPreparedSchemaAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string schema, CancellationToken ct = default)
    {
        await VerifyPreparedSchemaAsync(connection, transaction, schema, ct);
        var quoted = new NpgsqlCommandBuilder().QuoteIdentifier(schema);
        await using var command = new NpgsqlCommand($"SELECT max(version) FROM {quoted}.schema_migrations", connection, transaction);
        if (await command.ExecuteScalarAsync(ct) is not int version || version != CurrentVersion) throw InvalidSchema();
    }

    public static string BaselineChecksum => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(BaselineSql()))).ToLowerInvariant();
    public static string ExternalChecksum => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MigrationSql(2)))).ToLowerInvariant();
    public static string RecoveryChecksum => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MigrationSql(3)))).ToLowerInvariant();
    public static string LifecycleChecksum => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MigrationSql(4)))).ToLowerInvariant();
    public static string WebChecksum => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MigrationSql(5)))).ToLowerInvariant();
    public static string PushChecksum => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MigrationSql(6)))).ToLowerInvariant();

    public static async Task VerifyPreparedSchemaAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string schema, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        var fingerprint = await AccountsSchemaShape.FingerprintAsync(connection, transaction, schema, ct);
        var shapeVersion = fingerprint switch
        {
            AccountsSchemaShape.ExpectedFingerprint => 1,
            AccountsSchemaShape.ExternalFingerprint => 2,
            AccountsSchemaShape.RecoveryFingerprint => 3,
            AccountsSchemaShape.LifecycleFingerprint => 4,
            AccountsSchemaShape.WebFingerprint => 5,
            AccountsSchemaShape.PushFingerprint => 6,
            _ => throw InvalidSchema()
        };
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(schema);
        await using var history = new NpgsqlCommand($"SELECT version,checksum FROM {quotedSchema}.schema_migrations ORDER BY version", connection, transaction);
        await using var reader = await history.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.GetInt32(0) != 1 || reader.GetString(1) != BaselineChecksum)
            throw InvalidSchema();
        var version = 1;
        if (await reader.ReadAsync(ct))
        {
            if (reader.GetInt32(0) != 2 || reader.GetString(1) != ExternalChecksum) throw InvalidSchema();
            version = 2;
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetInt32(0) != 3 || reader.GetString(1) != RecoveryChecksum) throw InvalidSchema();
                version = 3;
                if (await reader.ReadAsync(ct))
                {
                    if (reader.GetInt32(0) != 4 || reader.GetString(1) != LifecycleChecksum) throw InvalidSchema();
                    version = 4;
                    if (await reader.ReadAsync(ct))
                    {
                        if (reader.GetInt32(0) != 5 || reader.GetString(1) != WebChecksum) throw InvalidSchema();
                        version = 5;
                        if (await reader.ReadAsync(ct))
                        {
                            if (reader.GetInt32(0) != 6 || reader.GetString(1) != PushChecksum || await reader.ReadAsync(ct)) throw InvalidSchema();
                            version = 6;
                        }
                    }
                }
            }
        }
        await reader.DisposeAsync();
        if (version != shapeVersion) throw InvalidSchema();
    }

    public async Task EnsureAsync(CancellationToken ct = default, int targetVersion = CurrentVersion)
    {
        if (targetVersion is not (1 or 2 or 3 or 4 or 5 or 6)) throw new ArgumentOutOfRangeException(nameof(targetVersion));
        await using var connection = dataSource.CreateMigrationConnection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await using (var session = new NpgsqlCommand("SET LOCAL search_path = pg_catalog; SET LOCAL TIME ZONE 'UTC'", connection, transaction))
            await session.ExecuteNonQueryAsync(ct);
        var lockBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"zapara.accounts.migrations\n{connection.Database}\n{configuration.Schema}"));
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction))
        {
            command.Parameters.AddWithValue("key", BinaryPrimitives.ReadInt64BigEndian(lockBytes));
            await command.ExecuteNonQueryAsync(ct);
        }
        await using (var command = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", connection, transaction))
        {
            command.Parameters.AddWithValue("schema", configuration.Schema);
            if ((long)(await command.ExecuteScalarAsync(ct))! != 1) throw InvalidSchema();
        }
        var objects = await AccountsSchemaShape.ObjectCountAsync(connection, transaction, configuration.Schema, ct);
        if (objects == 0)
        {
            await using var baseline = new NpgsqlCommand(BaselineSql().Replace("__SCHEMA__", configuration.QuotedSchema, StringComparison.Ordinal), connection, transaction);
            await baseline.ExecuteNonQueryAsync(ct);
            await using var record = new NpgsqlCommand($"INSERT INTO {configuration.QuotedSchema}.schema_migrations VALUES (1,@checksum,CURRENT_TIMESTAMP)", connection, transaction);
            record.Parameters.AddWithValue("checksum", BaselineChecksum);
            await record.ExecuteNonQueryAsync(ct);
            await VerifyPreparedSchemaAsync(connection, transaction, configuration.Schema, ct);
        }
        else
        {
            await VerifyPreparedSchemaAsync(connection, transaction, configuration.Schema, ct);
        }
        await using var current = new NpgsqlCommand($"SELECT max(version) FROM {configuration.QuotedSchema}.schema_migrations", connection, transaction);
        var version = (int)(await current.ExecuteScalarAsync(ct))!;
        if (version > targetVersion) throw InvalidSchema();
        if (version == 1 && targetVersion >= 2)
        {
            await using var migration = new NpgsqlCommand(MigrationSql(2).Replace("__SCHEMA__", configuration.QuotedSchema, StringComparison.Ordinal), connection, transaction);
            await migration.ExecuteNonQueryAsync(ct);
            await using var record = new NpgsqlCommand($"INSERT INTO {configuration.QuotedSchema}.schema_migrations VALUES (2,@checksum,CURRENT_TIMESTAMP)", connection, transaction);
            record.Parameters.AddWithValue("checksum", ExternalChecksum);
            await record.ExecuteNonQueryAsync(ct);
            await VerifyPreparedSchemaAsync(connection, transaction, configuration.Schema, ct);
            version = 2;
        }
        if (version == 2 && targetVersion >= 3)
        {
            await using var migration = new NpgsqlCommand(MigrationSql(3).Replace("__SCHEMA__", configuration.QuotedSchema, StringComparison.Ordinal), connection, transaction);
            await migration.ExecuteNonQueryAsync(ct);
            await using var record = new NpgsqlCommand($"INSERT INTO {configuration.QuotedSchema}.schema_migrations VALUES (3,@checksum,CURRENT_TIMESTAMP)", connection, transaction);
            record.Parameters.AddWithValue("checksum", RecoveryChecksum);
            await record.ExecuteNonQueryAsync(ct);
            await VerifyPreparedSchemaAsync(connection, transaction, configuration.Schema, ct);
            version = 3;
        }
        if (version == 3 && targetVersion >= 4)
        {
            await using var migration = new NpgsqlCommand(MigrationSql(4).Replace("__SCHEMA__", configuration.QuotedSchema, StringComparison.Ordinal), connection, transaction);
            await migration.ExecuteNonQueryAsync(ct);
            await using var record = new NpgsqlCommand($"INSERT INTO {configuration.QuotedSchema}.schema_migrations VALUES (4,@checksum,CURRENT_TIMESTAMP)", connection, transaction);
            record.Parameters.AddWithValue("checksum", LifecycleChecksum);
            await record.ExecuteNonQueryAsync(ct);
            await VerifyPreparedSchemaAsync(connection, transaction, configuration.Schema, ct);
            version = 4;
        }
        if (version == 4 && targetVersion >= 5)
        {
            await using var migration = new NpgsqlCommand(MigrationSql(5).Replace("__SCHEMA__", configuration.QuotedSchema, StringComparison.Ordinal), connection, transaction);
            await migration.ExecuteNonQueryAsync(ct);
            await using var record = new NpgsqlCommand($"INSERT INTO {configuration.QuotedSchema}.schema_migrations VALUES (5,@checksum,CURRENT_TIMESTAMP)", connection, transaction);
            record.Parameters.AddWithValue("checksum", WebChecksum);
            await record.ExecuteNonQueryAsync(ct);
            await VerifyPreparedSchemaAsync(connection, transaction, configuration.Schema, ct);
            version = 5;
        }
        if (version == 5 && targetVersion >= 6)
        {
            await using var migration = new NpgsqlCommand(MigrationSql(6).Replace("__SCHEMA__", configuration.QuotedSchema, StringComparison.Ordinal), connection, transaction);
            await migration.ExecuteNonQueryAsync(ct);
            await using var record = new NpgsqlCommand($"INSERT INTO {configuration.QuotedSchema}.schema_migrations VALUES (6,@checksum,CURRENT_TIMESTAMP)", connection, transaction);
            record.Parameters.AddWithValue("checksum", PushChecksum);
            await record.ExecuteNonQueryAsync(ct);
            await VerifyPreparedSchemaAsync(connection, transaction, configuration.Schema, ct);
        }
        await transaction.CommitAsync(ct);
    }

    private static string BaselineSql() => MigrationSql(1);

    internal static string MigrationSql(int version)
    {
        var name = version switch
        {
            1 => "Zapara.Server.Accounts.Sql.001_accounts.sql",
            2 => "Zapara.Server.Accounts.Sql.002_external_login.sql",
            3 => "Zapara.Server.Accounts.Sql.003_recovery.sql",
            4 => "Zapara.Server.Accounts.Sql.004_lifecycle.sql",
            5 => "Zapara.Server.Accounts.Sql.005_web_sessions.sql",
            6 => "Zapara.Server.Accounts.Sql.006_web_push.sql",
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };
        using var stream = typeof(AccountsMigrations).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Не найдена миграция Accounts.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    internal static InvalidOperationException InvalidSchema() => new("Схема Accounts не соответствует известной миграции.");
}
