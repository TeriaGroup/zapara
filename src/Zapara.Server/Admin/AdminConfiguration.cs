using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Zapara.Server.Accounts;
using Zapara.Server.Communities;

namespace Zapara.Server.Admin;

public sealed class AdminConfiguration
{
    private AdminConfiguration(string schema, AccountsConfiguration accounts, CommunitiesConfiguration communities)
        => (Schema, Accounts, Communities) = (schema, accounts, communities);

    public string Schema { get; }
    public string QuotedSchema => $"\"{Schema}\"";
    public AccountsConfiguration Accounts { get; }
    public CommunitiesConfiguration Communities { get; }

    public static bool IsEnabled(IConfiguration configuration)
    {
        var raw = configuration["Admin:Enabled"];
        if (raw is null) return false;
        return bool.TryParse(raw, out var enabled) ? enabled : throw new ArgumentException("Недопустимый Admin:Enabled.");
    }

    public static AdminConfiguration FromConfiguration(IConfiguration configuration, bool forMigration = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var enabled = IsEnabled(configuration);
        if (enabled && !AccountsConfiguration.IsEnabled(configuration)) throw new ArgumentException("Admin требует Accounts.");
        if (enabled && !CommunitiesConfiguration.IsEnabled(configuration)) throw new ArgumentException("Admin требует Communities.");
        if (!enabled && !forMigration) throw new ArgumentException("Модуль Admin отключён.");
        var accounts = AccountsConfiguration.FromConfiguration(configuration, forMigration);
        var communities = CommunitiesConfiguration.FromConfiguration(configuration, forMigration);
        var schema = configuration["Admin:Schema"];
        if (schema is null || !Regex.IsMatch(schema, @"\A[a-z][a-z0-9_]{0,62}\z", RegexOptions.CultureInvariant) ||
            schema == accounts.Schema || schema == communities.Schema || schema == configuration["Timetable:Schema"] ||
            schema is "public" or "information_schema" || schema.StartsWith("pg_", StringComparison.Ordinal))
            throw new ArgumentException("Недопустимый Admin:Schema.");
        return new(schema, accounts, communities);
    }

    public override string ToString() => "AdminConfiguration { [REDACTED] }";
}
