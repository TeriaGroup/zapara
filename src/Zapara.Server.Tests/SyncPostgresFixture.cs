using Microsoft.Extensions.Configuration;
using Xunit;
using Zapara.Server.Sync;

namespace Zapara.Server.Tests;

public sealed class SyncPostgresFixture : IAsyncDisposable
{
    public AccountsPostgresFixture Accounts { get; }
    public string Schema { get; } = "sync_test_" + Guid.NewGuid().ToString("N");
    public string QuotedSchema => $"\"{Schema}\"";
    public SyncConfiguration Configuration { get; }
    public SyncMigrations Migrations => new(Accounts.DataSource, Configuration);
    private SyncPostgresFixture(AccountsPostgresFixture accounts, bool? accountsContainsSync = null)
    {
        Accounts = accounts;
        if (accountsContainsSync is not null)
            Schema = accountsContainsSync.Value ? accounts.Schema[4..] : "sync_" + accounts.Schema;
        Configuration = Options(Schema, accounts.Schema);
    }
    public static SyncConfiguration Options(string schema, string accountsSchema)
        => SyncConfiguration.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sync:Enabled"] = "true", ["Sync:Schema"] = schema,
            ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = accountsSchema,
            ["ConnectionStrings:Accounts"] = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")
        }).Build(), true);
    public static async Task<SyncPostgresFixture> CreateAsync(bool initialize = false, bool? accountsContainsSync = null)
    {
        var db = new SyncPostgresFixture(await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true), accountsContainsSync);
        try
        {
            await db.Accounts.ExecuteAsync($"CREATE SCHEMA {db.QuotedSchema}");
            Console.WriteLine($"CREATE {db.Schema}");
            if (initialize) await db.Migrations.EnsureAsync(TestContext.Current.CancellationToken);
            return db;
        }
        catch { await db.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            await Accounts.ExecuteAsync($"DROP SCHEMA IF EXISTS {QuotedSchema} CASCADE");
            var count = await Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{Schema}'");
            Console.WriteLine($"TEARDOWN {Schema} remaining={count}");
            Assert.Equal(0L, count);
        }
        finally { await Accounts.DisposeAsync(); }
    }
}
