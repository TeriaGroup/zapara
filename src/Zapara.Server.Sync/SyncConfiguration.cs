using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
using Zapara.Server.Accounts;

namespace Zapara.Server.Sync;

public sealed class SyncConfiguration
{
    private SyncConfiguration(string schema, AccountsConfiguration accounts) => (Schema, Accounts) = (schema, accounts);
    public string Schema { get; }
    public string QuotedSchema => $"\"{Schema}\"";
    public AccountsConfiguration Accounts { get; }
    public static bool IsEnabled(IConfiguration configuration)
    {
        var raw = configuration["Sync:Enabled"];
        if (raw is null) return false;
        return bool.TryParse(raw, out var enabled) ? enabled : throw new ArgumentException("Недопустимый Sync:Enabled.");
    }
    public static SyncConfiguration FromConfiguration(IConfiguration configuration, bool forMigration = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var enabled = IsEnabled(configuration);
        if (enabled && !AccountsConfiguration.IsEnabled(configuration)) throw new ArgumentException("Sync требует Accounts.");
        if (!enabled && !forMigration) throw new ArgumentException("Модуль Sync отключён.");
        var accounts = AccountsConfiguration.FromConfiguration(configuration, forMigration);
        var schema = configuration["Sync:Schema"];
        if (schema is null || !Regex.IsMatch(schema, @"\A[a-z][a-z0-9_]{0,62}\z", RegexOptions.CultureInvariant) ||
            schema == accounts.Schema || schema == configuration["Timetable:Schema"] ||
            schema is "public" or "information_schema" || schema.StartsWith("pg_", StringComparison.Ordinal))
            throw new ArgumentException("Недопустимый Sync:Schema.");
        return new(schema, accounts);
    }
    public override string ToString() => "SyncConfiguration { [REDACTED] }";
}
