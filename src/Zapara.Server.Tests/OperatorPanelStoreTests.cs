using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Admin;
using Zapara.Server.Communities;

namespace Zapara.Server.Tests;

public sealed class OperatorPanelStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Dsn => Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")
        ?? throw new InvalidOperationException("ZAPARA_TEST_POSTGRES is required.");

    [Fact]
    public async Task Prepare_panel_schemas()
    {
        await using var connection = new NpgsqlConnection(Dsn);
        await connection.OpenAsync(Ct);
        foreach (var schema in new[] { "accounts", "admin", "communities" })
        {
            await using var command = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS {schema}", connection);
            await command.ExecuteNonQueryAsync(Ct);
        }
        var accounts = AccountsConfiguration.FromConfiguration(Config(), true);
        await using var source = accounts.CreateDataSource();
        await new AccountsMigrations(source, accounts).EnsureAsync(Ct);
        await new CommunitiesMigrations(source, CommunitiesConfiguration.FromConfiguration(Config(), true)).EnsureAsync(Ct);
        await new AdminMigrations(source, AdminConfiguration.FromConfiguration(Config(), true)).EnsureAsync(Ct);
    }

    [Fact]
    public async Task Saved_registration_setting_changes_a_fresh_register_request()
    {
        var schema = "op_" + Guid.NewGuid().ToString("N");
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        try
        {
            await db.ExecuteAsync($"CREATE SCHEMA {schema}");
            await db.ExecuteAsync($"""
                CREATE TABLE {schema}.system_settings (
                    key text PRIMARY KEY,
                    value text NOT NULL,
                    updated_at timestamptz NOT NULL
                )
                """);
            await db.ExecuteAsync($"INSERT INTO {schema}.system_settings(key,value,updated_at) VALUES ('registration_enabled','false',CURRENT_TIMESTAMP)");
            await using (var closed = new AccountApiTestHost(db, overrides: new() { ["Operator:Schema"] = schema }))
                await closed.Send("POST", "/auth/register", 503, raw: "{}", code: "registration_unavailable");
            await db.ExecuteAsync($"UPDATE {schema}.system_settings SET value='true' WHERE key='registration_enabled'");
            await using (var open = new AccountApiTestHost(db, overrides: new() { ["Operator:Schema"] = schema }))
                await open.Send("POST", "/auth/register", 400, raw: "{}", code: "invalid_request");
            await using (var production = new AccountApiTestHost(db, "Production", new() { ["Operator:Schema"] = schema }))
                await production.Send("POST", "/auth/register", 400, raw: "{}", code: "invalid_request");
        }
        finally
        {
            await db.ExecuteAsync($"DROP SCHEMA IF EXISTS {schema} CASCADE");
        }
    }

    [Fact]
    public async Task Default_operator_store_matches_register_route()
    {
        bool? stored = null;
        await using (var connection = new NpgsqlConnection(Dsn))
        {
            await connection.OpenAsync(Ct);
            if (OperatorSettings.TryReadRegistration(connection, "operator", out var enabled)) stored = enabled;
        }
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        var closed = stored == false;
        await host.Send("POST", "/auth/register", closed ? 503 : 400, raw: "{}", code: closed ? "registration_unavailable" : "invalid_request");
        Mark("OPERATOR_REGISTRATION=" + (stored is null ? "absent" : stored.Value ? "true" : "false"));
        Mark("REGISTER_STATUS=" + (closed ? 503 : 400));
    }

    [Fact]
    public async Task Panel_user_authentication_follows_account_status()
    {
        var username = Environment.GetEnvironmentVariable("ZAPARA_PANEL_USERNAME");
        var password = Environment.GetEnvironmentVariable("ZAPARA_PANEL_PASSWORD");
        var expect = Environment.GetEnvironmentVariable("ZAPARA_PANEL_EXPECT");
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var overrides = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(username)) overrides["Accounts:Schema"] = "accounts";
        await using var host = new AccountApiTestHost(db, overrides: overrides);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            await host.Send("POST", "/auth/login", 400, raw: "{}", code: "invalid_request");
            Mark("PANEL_LOGIN=unknown");
            return;
        }
        var body = new
        {
            username,
            password,
            device = new { deviceId = Guid.NewGuid(), deviceName = "Панель", platform = "windows" }
        };
        if (expect == "active")
        {
            await host.Send("POST", "/auth/login", 200, body);
            Mark("PANEL_LOGIN=ok");
            return;
        }
        await host.Send("POST", "/auth/login", 401, body, code: "invalid_credentials");
        Mark("PANEL_LOGIN=blocked");
    }

    private static void Mark(string line)
    {
        Console.WriteLine(line);
        var path = Environment.GetEnvironmentVariable("ZAPARA_PANEL_RESULT");
        if (!string.IsNullOrWhiteSpace(path)) File.AppendAllText(path, line + Environment.NewLine);
    }

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Accounts:Enabled"] = "true",
        ["Accounts:Schema"] = "accounts",
        ["Communities:Enabled"] = "true",
        ["Communities:Schema"] = "communities",
        ["Admin:Enabled"] = "true",
        ["Admin:Schema"] = "admin",
        ["ConnectionStrings:Accounts"] = Dsn,
    }).Build();
}
