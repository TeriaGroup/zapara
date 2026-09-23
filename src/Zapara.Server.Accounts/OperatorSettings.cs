using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Zapara.Server.Accounts;

public static class OperatorSettings
{
    public const string RegistrationKey = "registration_enabled";
    private static readonly Regex SchemaName = new(@"\A[a-z][a-z0-9_]{0,62}\z", RegexOptions.CultureInvariant);

    public static string Schema(IConfiguration? configuration)
    {
        var raw = configuration?["Operator:Schema"];
        return !string.IsNullOrWhiteSpace(raw) && SchemaName.IsMatch(raw) ? raw : "operator";
    }

    public static bool TryReadRegistration(NpgsqlConnection connection, string schema, out bool enabled)
    {
        enabled = false;
        if (!SchemaName.IsMatch(schema)) return false;
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) connection.Open();
        using var probe = new NpgsqlCommand("SELECT to_regclass(@name)::text", connection);
        probe.Parameters.AddWithValue("name", schema + ".system_settings");
        if (probe.ExecuteScalar() is not string) return false;
        using var command = new NpgsqlCommand($"SELECT value FROM {schema}.system_settings WHERE key = @key", connection);
        command.Parameters.AddWithValue("key", RegistrationKey);
        var value = command.ExecuteScalar();
        if (value is null or DBNull) return false;
        enabled = value is string text && bool.TryParse(text, out var parsed) && parsed;
        return true;
    }

    public static IReadOnlyDictionary<string, string> ReadAll(NpgsqlConnection connection, string schema)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!SchemaName.IsMatch(schema)) return found;
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) connection.Open();
        using var probe = new NpgsqlCommand("SELECT to_regclass(@name)::text", connection);
        probe.Parameters.AddWithValue("name", schema + ".system_settings");
        if (probe.ExecuteScalar() is not string) return found;
        using var command = new NpgsqlCommand($"SELECT key, value FROM {schema}.system_settings", connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(0) is { Length: > 0 } key && reader.GetValue(1) is string value)
                found[key] = value;
        }
        return found;
    }
}
