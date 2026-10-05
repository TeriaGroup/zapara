using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Text.RegularExpressions;

namespace Zapara.Server.Accounts;

public sealed class AccountsConfiguration
{
    private readonly string connectionString;
    private readonly string? migrationConnectionString;
    private AccountsConfiguration(string connectionString, string? migrationConnectionString, string schema)
        => (this.connectionString, this.migrationConnectionString, Schema) = (connectionString, migrationConnectionString, schema);

    public string Schema { get; }
    public string QuotedSchema => $"\"{Schema}\"";

    public static bool IsEnabled(IConfiguration configuration)
    {
        var raw = configuration["Accounts:Enabled"];
        if (raw is null) return false;
        if (bool.TryParse(raw, out var enabled)) return enabled;
        throw new ArgumentException("Недопустимый Accounts:Enabled.");
    }

    public static AccountsConfiguration FromConfiguration(IConfiguration configuration, bool forMigration = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var enabled = IsEnabled(configuration);
        if (!enabled && !forMigration) throw new ArgumentException("Модуль Accounts отключён.");
        var schema = configuration["Accounts:Schema"];
        if (schema is null || !Regex.IsMatch(schema, @"\A[a-z][a-z0-9_]{0,62}\z", RegexOptions.CultureInvariant) ||
            schema == configuration["Timetable:Schema"] || schema is "public" or "information_schema" || schema.StartsWith("pg_", StringComparison.Ordinal))
            throw new ArgumentException("Недопустимый Accounts:Schema.");
        var raw = configuration.GetConnectionString("Accounts") ?? configuration.GetConnectionString("Timetable");
        var runtime = Parse(raw);
        var migrationRaw = configuration.GetConnectionString("AccountsMigration");
        // Local operator proof may reuse its DSN. Remote operators must supply an explicit override.
        var local = new NpgsqlConnectionStringBuilder(runtime).Host is "127.0.0.1" or "localhost" or "::1";
        var migration = migrationRaw is not null ? Parse(migrationRaw) : local ? runtime : null;
        if (forMigration && migration is null)
            throw new ArgumentException("Для миграции требуется отдельная конфигурация подключения.");
        return new(runtime, migration, schema);
    }

    private static string Parse(string? raw)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException();
            var builder = new NpgsqlConnectionStringBuilder(raw)
            {
                Pooling = true, IncludeErrorDetail = false, LogParameters = false, PersistSecurityInfo = false
            };
            if (string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Database)) throw new ArgumentException();
            return builder.ConnectionString;
        }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
        {
            throw new ArgumentException("Недопустимое подключение Accounts.");
        }
    }

    public AccountsDataSource CreateDataSource() => new(connectionString, migrationConnectionString);
    public override string ToString() => "AccountsConfiguration { [REDACTED] }";
}
