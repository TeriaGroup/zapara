using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Zapara.Contracts.Accounts;
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
    public async Task Panel_settings_drive_capabilities()
    {
        await using var connection = new NpgsqlConnection(Dsn);
        await connection.OpenAsync(Ct);
        var stored = OperatorSettings.ReadAll(connection, "operator");
        if (!stored.TryGetValue("vk_client_id", out var client) || client != "vk-app-2")
        {
            await OwnSchemaDrivesCapabilities();
            return;
        }
        var configuration = new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "true",
            ["Accounts:Schema"] = "accounts",
            ["ConnectionStrings:Accounts"] = Dsn,
            ["Operator:Schema"] = "operator",
        };
        await using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Accounts:Enabled", "true");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
        });
        using var http = factory.CreateClient();
        using var first = await http.GetAsync("/api/v1/auth/capabilities", Ct);
        var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync(Ct)).RootElement;
        Mark("CAPABILITIES_VK=" + (firstBody.GetProperty("vk").GetBoolean() ? "true" : "false"));
        Mark("CAPABILITIES_YANDEX=" + (firstBody.GetProperty("yandex").GetBoolean() ? "true" : "false"));
        Mark("STORAGE=" + (factory.Services.GetRequiredService<Zapara.Server.Storage.RoutingObjectStore>().RemoteConfigured() ? "s3" : "local"));
        await using (var update = new NpgsqlCommand("UPDATE operator.system_settings SET value='true' WHERE key='vk_enabled'", connection))
            await update.ExecuteNonQueryAsync(Ct);
        using var second = await http.GetAsync("/api/v1/auth/capabilities", Ct);
        var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync(Ct)).RootElement;
        Mark("CAPABILITIES_VK_AFTER=" + (secondBody.GetProperty("vk").GetBoolean() ? "true" : "false"));
        await using (var clear = new NpgsqlCommand("UPDATE operator.system_settings SET value='' WHERE key='s3_bucket'", connection))
            await clear.ExecuteNonQueryAsync(Ct);
        Mark("STORAGE_AFTER=" + (factory.Services.GetRequiredService<Zapara.Server.Storage.RoutingObjectStore>().RemoteConfigured() ? "s3" : "local"));
        await using (var restoreVk = new NpgsqlCommand("UPDATE operator.system_settings SET value='false' WHERE key='vk_enabled'", connection))
            await restoreVk.ExecuteNonQueryAsync(Ct);
        await using (var restoreBucket = new NpgsqlCommand("UPDATE operator.system_settings SET value='zapara-bucket' WHERE key='s3_bucket'", connection))
            await restoreBucket.ExecuteNonQueryAsync(Ct);
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

    [Fact]
    public async Task Panel_password_change_rejects_outstanding_reset()
    {
        var username = Environment.GetEnvironmentVariable("ZAPARA_PANEL_USERNAME");
        var password = Environment.GetEnvironmentVariable("ZAPARA_PANEL_PASSWORD");
        var token = Environment.GetEnvironmentVariable("ZAPARA_PANEL_RESET_TOKEN");
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var overrides = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(username)) overrides["Accounts:Schema"] = "accounts";
        await using var host = new AccountApiTestHost(db, overrides: overrides);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(token))
        {
            await host.Send("POST", "/auth/password-reset/confirm", 400, raw: "{}", code: "invalid_request");
            Mark("RESET_CONFIRM=unknown");
            return;
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        await using (var connection = new NpgsqlConnection(Dsn))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand(
                "SELECT consumed_at IS NOT NULL FROM accounts.password_reset_tokens WHERE token_hash=@hash", connection);
            command.Parameters.Add(new NpgsqlParameter("hash", NpgsqlTypes.NpgsqlDbType.Bytea) { Value = hash });
            var consumed = await command.ExecuteScalarAsync(Ct);
            Assert.True(consumed is true);
        }
        await host.Send("POST", "/auth/password-reset/confirm", 400,
            new PasswordResetConfirmRequest(token, "Reset-must-not-apply-1"), code: "invalid_request");
        Mark("RESET_CONFIRM=rejected");
        await host.Send("POST", "/auth/login", 200, new
        {
            username,
            password,
            device = new { deviceId = Guid.NewGuid(), deviceName = "Панель", platform = "windows" }
        });
        Mark("PANEL_PASSWORD=kept");
    }

    private static async Task OwnSchemaDrivesCapabilities()
    {
        var schema = "op_" + Guid.NewGuid().ToString("N");
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        await db.ExecuteAsync($"CREATE SCHEMA {schema}");
        await db.ExecuteAsync($"""
            CREATE TABLE {schema}.system_settings (key text PRIMARY KEY, value text NOT NULL, updated_at timestamptz NOT NULL)
            """);
        await db.ExecuteAsync($"""
            INSERT INTO {schema}.system_settings(key,value,updated_at) VALUES
            ('vk_enabled','false',CURRENT_TIMESTAMP),
            ('vk_client_id','vk-app-2',CURRENT_TIMESTAMP),
            ('vk_callback','https://voen.teriahost.ru/auth/vk/callback',CURRENT_TIMESTAMP),
            ('yandex_enabled','true',CURRENT_TIMESTAMP),
            ('yandex_client_id','ya-app',CURRENT_TIMESTAMP),
            ('yandex_callback','https://voen.teriahost.ru/auth/yandex/callback',CURRENT_TIMESTAMP),
            ('s3_endpoint','http://127.0.0.1:9',CURRENT_TIMESTAMP),
            ('s3_region','ru-central1',CURRENT_TIMESTAMP),
            ('s3_bucket','zapara-bucket',CURRENT_TIMESTAMP),
            ('s3_access_key','AKIAEXAMPLE',CURRENT_TIMESTAMP),
            ('s3_secret','s3-secret-value',CURRENT_TIMESTAMP)
            """);
        var configuration = new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "true",
            ["Accounts:Schema"] = db.Schema,
            ["ConnectionStrings:Accounts"] = Dsn,
            ["Operator:Schema"] = schema,
        };
        await using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Accounts:Enabled", "true");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
        });
        using var client = factory.CreateClient();
        using var first = await client.GetAsync("/api/v1/auth/capabilities", Ct);
        var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync(Ct)).RootElement;
        Assert.False(firstBody.GetProperty("vk").GetBoolean());
        Assert.True(firstBody.GetProperty("yandex").GetBoolean());
        Assert.True(factory.Services.GetRequiredService<Zapara.Server.Storage.RoutingObjectStore>().RemoteConfigured());
        await db.ExecuteAsync($"UPDATE {schema}.system_settings SET value='true' WHERE key='vk_enabled'");
        using var second = await client.GetAsync("/api/v1/auth/capabilities", Ct);
        var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync(Ct)).RootElement;
        Assert.True(secondBody.GetProperty("vk").GetBoolean());
        await db.ExecuteAsync($"UPDATE {schema}.system_settings SET value='' WHERE key='s3_bucket'");
        Assert.False(factory.Services.GetRequiredService<Zapara.Server.Storage.RoutingObjectStore>().RemoteConfigured());
        await db.ExecuteAsync($"DROP SCHEMA IF EXISTS {schema} CASCADE");
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
