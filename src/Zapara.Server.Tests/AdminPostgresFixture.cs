using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Admin;
using Zapara.Server.Communities;

namespace Zapara.Server.Tests;

public sealed class AdminPostgresFixture : IAsyncDisposable
{
    public CommunityPostgresFixture Communities { get; }
    public AccountsPostgresFixture Accounts => Communities.Accounts;
    public string Schema { get; } = "adm_test_" + Guid.NewGuid().ToString("N");
    public string QuotedSchema => $"\"{Schema}\"";
    public AdminConfiguration Configuration { get; }
    public AdminMigrations Migrations => new(Accounts.DataSource, Configuration);
    internal AccountClock Clock { get; } = new();

    private AdminPostgresFixture(CommunityPostgresFixture communities)
    {
        Communities = communities;
        Configuration = Options(Schema, communities.Schema, communities.Accounts.Schema);
    }

    public static AdminConfiguration Options(string admin, string communities, string accounts)
        => AdminConfiguration.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Admin:Enabled"] = "true", ["Admin:Schema"] = admin,
            ["Communities:Enabled"] = "true", ["Communities:Schema"] = communities,
            ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = accounts,
            ["ConnectionStrings:Accounts"] = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")
        }).Build(), true);

    public static async Task<AdminPostgresFixture> CreateAsync(bool initialize = false)
    {
        var db = new AdminPostgresFixture(await CommunityPostgresFixture.CreateAsync(true));
        try
        {
            await db.Accounts.ExecuteAsync($"CREATE SCHEMA {db.QuotedSchema}");
            Console.WriteLine($"CREATE {db.Schema}");
            if (initialize) await db.Migrations.EnsureAsync(TestContext.Current.CancellationToken);
            return db;
        }
        catch { await db.DisposeAsync(); throw; }
    }

    public AccountService AccountService => new(Accounts.DataSource, Accounts.Configuration, Clock);

    public async Task<long> TableCountAsync()
        => await Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{Schema}'");

    public async Task ExecuteAsync(string sql) => await Accounts.ExecuteAsync(sql);

    public async Task<T> ScalarAsync<T>(string sql) => await Accounts.ScalarAsync<T>(sql);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Accounts.ExecuteAsync($"DROP SCHEMA IF EXISTS {QuotedSchema} CASCADE");
            var count = await Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{Schema}'");
            Console.WriteLine($"TEARDOWN {Schema} remaining={count}");
            Assert.Equal(0L, count);
        }
        finally { await Communities.DisposeAsync(); }
    }
}
