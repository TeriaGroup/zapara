using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using Zapara.Server.Accounts;

namespace Zapara.Server.Tests;

public sealed class AccountsPostgresFixture : IAsyncDisposable
{
    private readonly Action<string> receipt;
    private bool created;
    public string Schema { get; } = "acc_test_" + Guid.NewGuid().ToString("N");
    public string QuotedSchema => $"\"{Schema}\"";
    public AccountsConfiguration Configuration { get; }
    public AccountsDataSource DataSource { get; }
    public AccountsMigrations Migrations => new(DataSource, Configuration);

    private AccountsPostgresFixture(Action<string> receipt)
    {
        this.receipt = receipt;
        var raw = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES");
        ValidateConnection(raw);
        Configuration = AccountsConfiguration.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = Schema,
                ["ConnectionStrings:Accounts"] = raw }).Build());
        DataSource = Configuration.CreateDataSource();
    }

    public static void ValidateConnection(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("ZAPARA_TEST_POSTGRES required; no skips.");
        NpgsqlConnectionStringBuilder builder;
        try { builder = new(raw); }
        catch (ArgumentException) { throw new InvalidOperationException("Unsafe fixture connection."); }
        if (builder.Database != "zapara_test" || builder.Host is not ("localhost" or "127.0.0.1") || builder.Port != 56432)
            throw new InvalidOperationException("Only approved local fixture database is allowed.");
    }

    public static async Task<AccountsPostgresFixture> CreateAsync(Action<string> receipt, bool initialize = false, int targetVersion = 4)
    {
        var db = new AccountsPostgresFixture(receipt);
        try
        {
            await db.ExecuteAsync($"CREATE SCHEMA {db.QuotedSchema}");
            db.created = true;
            receipt($"CREATE {db.Schema}");
            if (initialize) await db.Migrations.EnsureAsync(TestContext.Current.CancellationToken, targetVersion);
            return db;
        }
        catch { await db.DisposeAsync(); throw; }
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = DataSource.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = DataSource.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async Task<string> FingerprintAsync()
    {
        await using var connection = DataSource.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        return await AccountsSchemaShape.FingerprintAsync(connection, tx, Schema, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (created)
            {
                await ExecuteAsync($"DROP SCHEMA {QuotedSchema} CASCADE");
                var count = await ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{Schema}'");
                receipt($"TEARDOWN {Schema} remaining={count}");
                Assert.Equal(0, count);
                created = false;
            }
        }
        finally { await DataSource.DisposeAsync(); }
    }
}
