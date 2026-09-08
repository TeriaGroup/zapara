using System.Globalization;
using System.Text.Json.Nodes;
using Npgsql;

namespace Zapara.Server.Accounts;

public interface IAccountLifecycleParticipant
{
    string Module { get; }
    Task ContributeExportAsync(AccountLifecycleContext context, CancellationToken ct);
    Task DeleteOwnedDataAsync(AccountLifecycleContext context, CancellationToken ct);
}

public sealed class AccountExportDocument
{
    public AccountExportDocument(DateTimeOffset exportedAt)
    {
        Root = new JsonObject
        {
            ["exportedAt"] = Utc(exportedAt),
            ["profile"] = null,
            ["linkedProviders"] = new JsonArray(),
            ["devices"] = new JsonArray(),
            ["privateSync"] = new JsonArray(),
            ["memberships"] = new JsonArray(),
            ["contributions"] = new JsonArray(),
            ["completions"] = new JsonArray(),
            ["votes"] = new JsonArray()
        };
    }

    public JsonObject Root { get; }
    public JsonArray LinkedProviders => (JsonArray)Root["linkedProviders"]!;
    public JsonArray Devices => (JsonArray)Root["devices"]!;
    public JsonArray PrivateSync => (JsonArray)Root["privateSync"]!;
    public JsonArray Memberships => (JsonArray)Root["memberships"]!;
    public JsonArray Contributions => (JsonArray)Root["contributions"]!;
    public JsonArray Completions => (JsonArray)Root["completions"]!;
    public JsonArray Votes => (JsonArray)Root["votes"]!;

    public void SetProfile(JsonObject profile) => Root["profile"] = profile;

    public static JsonNode Utc(DateTimeOffset value)
        => JsonValue.Create(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture))!;

    public static JsonNode Id(Guid value) => JsonValue.Create(value.ToString("D"))!;
}

public sealed class AccountLifecycleContext
{
    public AccountLifecycleContext(Guid userId, DateTimeOffset utcNow, NpgsqlConnection connection,
        NpgsqlTransaction transaction, AccountExportDocument export, CancellationToken cancellation)
    {
        UserId = userId;
        UtcNow = utcNow;
        Connection = connection;
        Transaction = transaction;
        Export = export;
        Cancellation = cancellation;
    }

    public Guid UserId { get; }
    public DateTimeOffset UtcNow { get; }
    public NpgsqlConnection Connection { get; }
    public NpgsqlTransaction Transaction { get; }
    public AccountExportDocument Export { get; }
    public CancellationToken Cancellation { get; }

    public NpgsqlCommand Command(string sql, params object?[] values)
    {
        var command = new NpgsqlCommand(sql, Connection, Transaction);
        for (var i = 0; i < values.Length; i++)
            command.Parameters.AddWithValue("p" + i, values[i] ?? DBNull.Value);
        return command;
    }

    public async Task ExecuteAsync(string sql, params object?[] values)
    {
        await using var command = Command(sql, values);
        await command.ExecuteNonQueryAsync(Cancellation);
    }
}
