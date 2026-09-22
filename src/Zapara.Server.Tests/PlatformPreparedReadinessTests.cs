using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;
using Zapara.Server.Communities;
using Zapara.Server.Timetable;
using Vograph.Timetable;

namespace Zapara.Server.Tests;

public sealed class PlatformPreparedReadinessTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Prepared_timetable_without_a_published_snapshot_is_not_ready_but_remains_live()
    {
        await using var timetable = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await using var host = Host(timetable.Schema);
        using var client = host.CreateClient();
        await AssertModule(client, "timetable", "missing", HttpStatusCode.ServiceUnavailable);
        using var live = await client.GetAsync("/health/live", Ct);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(0L, await timetable.ScalarAsync<long>($"SELECT count(*) FROM {timetable.QuotedSchema}.refresh_attempts", Ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Empty_or_historical_Accounts_namespace_is_not_ready_and_never_upgraded(int version)
    {
        await using var timetable = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await ApiTestFactory.PublishAsync(timetable);
        await using var accounts = await AccountsPostgresFixture.CreateAsync(output.WriteLine, initialize: version > 0, targetVersion: Math.Max(1, version));
        var objects = await accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_class WHERE relnamespace='{accounts.Schema}'::regnamespace");
        await using var host = Host(timetable.Schema, accounts.Schema);
        using var client = host.CreateClient();
        await AssertModule(client, "accounts", "missing", HttpStatusCode.ServiceUnavailable);
        Assert.Equal(objects, await accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_class WHERE relnamespace='{accounts.Schema}'::regnamespace"));
        if (version > 0) Assert.Equal(version, await accounts.ScalarAsync<int>($"SELECT max(version) FROM {accounts.QuotedSchema}.schema_migrations"));
    }

    [Fact]
    public async Task Current_Accounts_checksum_drift_is_not_repaired_by_readiness()
    {
        await using var timetable = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await ApiTestFactory.PublishAsync(timetable);
        await using var accounts = await AccountsPostgresFixture.CreateAsync(output.WriteLine, initialize: true);
        await accounts.ExecuteAsync($"UPDATE {accounts.QuotedSchema}.schema_migrations SET checksum=repeat('0',64) WHERE version=6");
        await using var host = Host(timetable.Schema, accounts.Schema);
        using var client = host.CreateClient();
        await AssertModule(client, "accounts", "missing", HttpStatusCode.ServiceUnavailable);
        Assert.Equal(new string('0', 64), await accounts.ScalarAsync<string>($"SELECT checksum FROM {accounts.QuotedSchema}.schema_migrations WHERE version=6"));
    }

    [Fact]
    public async Task Missing_required_sync_table_is_not_ready()
    {
        await using var timetable = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await ApiTestFactory.PublishAsync(timetable);
        await using var sync = await SyncPostgresFixture.CreateAsync(initialize: true);
        await sync.Accounts.ExecuteAsync($"DROP TABLE {sync.QuotedSchema}.sync_receipts");
        await using var host = Host(timetable.Schema, sync.Accounts.Schema, sync.Schema);
        using var client = host.CreateClient();
        await AssertModule(client, "sync", "missing", HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Historical_Communities_version_is_not_ready_or_upgraded()
    {
        await using var timetable = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await ApiTestFactory.PublishAsync(timetable);
        await using var communities = await CommunityPostgresFixture.CreateAsync();
        await communities.Accounts.Migrations.EnsureAsync(Ct, 6);
        await communities.Migrations.EnsureAsync(Ct, 1);
        await using var host = Host(timetable.Schema, communities.Accounts.Schema, communities: communities.Schema);
        using var client = host.CreateClient();
        await AssertModule(client, "communities", "missing", HttpStatusCode.ServiceUnavailable);
        Assert.Equal(1, await communities.Accounts.ScalarAsync<int>($"SELECT max(version) FROM {communities.QuotedSchema}.schema_migrations"));
    }

    [Fact]
    public async Task Historical_timetable_schema_with_readable_data_is_not_current_and_is_not_upgraded()
    {
        await using var timetable = await PostgresFixture.CreateAsync(output.WriteLine, initialize: false, ct: Ct);
        using var stream = typeof(SnapshotStore).Assembly.GetManifestResourceStream("Zapara.Server.Timetable.Sql.001_timetable.sql")!;
        using var reader = new StreamReader(stream);
        await timetable.ExecuteAsync((await reader.ReadToEndAsync(Ct)).Replace("{{schema}}", timetable.QuotedSchema)
            .Replace("{{url}}", "'" + TimetableParser.DefaultUrl + "'"), Ct);
        await ApiTestFactory.PublishAsync(timetable);
        await using var host = Host(timetable.Schema);
        using var client = host.CreateClient();
        await AssertModule(client, "timetable", "missing", HttpStatusCode.ServiceUnavailable);
        Assert.Equal(1, await timetable.ScalarAsync<int>($"SELECT version FROM {timetable.QuotedSchema}.schema_version", Ct));
    }

    [Fact]
    public async Task Corrupt_wholly_empty_published_payload_does_not_claim_data_readiness()
    {
        await using var timetable = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var id = await ApiTestFactory.PublishAsync(timetable);
        await timetable.ExecuteAsync($$"""
            UPDATE {{timetable.QuotedSchema}}.snapshots SET payload=jsonb_set(jsonb_set(payload,'{lessons}','[]'::jsonb),
                '{groups}','[{"id":"g","name":"Группа","lessonCount":0}]'::jsonb) WHERE snapshot_id='{{id}}'
            """, Ct);
        await using var host = Host(timetable.Schema);
        using var client = host.CreateClient();
        await AssertModule(client, "timetable", "missing", HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Full_current_platform_is_ready_using_only_readonly_runtime_connections()
    {
        await using var timetable = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await ApiTestFactory.PublishAsync(timetable);
        await using var admin = await AdminPostgresFixture.CreateAsync(initialize: true);
        await admin.Accounts.Migrations.EnsureAsync(Ct, 6);
        var syncSchema = "ready_sync_" + Guid.NewGuid().ToString("N");
        await admin.Accounts.ExecuteAsync($"CREATE SCHEMA \"{syncSchema}\"");
        try
        {
            await new SyncMigrations(admin.Accounts.DataSource, SyncPostgresFixture.Options(syncSchema, admin.Accounts.Schema)).EnsureAsync(Ct);
            var before = await ApiTestFactory.DatabaseStateAsync(timetable);
            await using var host = Host(timetable.Schema, admin.Accounts.Schema, syncSchema, admin.Communities.Schema, admin.Schema);
            using var client = host.CreateClient();
            foreach (var module in new[] { "timetable", "accounts", "sync", "communities", "admin" })
                await AssertModule(client, module, "present", HttpStatusCode.OK);
            Assert.Equal(before, await ApiTestFactory.DatabaseStateAsync(timetable));
        }
        finally { await admin.Accounts.ExecuteAsync($"DROP SCHEMA \"{syncSchema}\" CASCADE"); }
    }

    [Fact]
    public async Task Unavailable_database_does_not_break_liveness_or_leak_connection_details()
    {
        await using var host = Host("unavailable_timetable", unavailable: true);
        using var client = host.CreateClient();
        using var live = await client.GetAsync("/health/live", Ct);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        await AssertModule(client, "timetable", "missing", HttpStatusCode.ServiceUnavailable);
    }

    private static WebApplicationFactory<Program> Host(string timetable, string? accounts = null, string? sync = null, string? communities = null, string? admin = null, bool unavailable = false)
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES"))
        { Options = "-c default_transaction_read_only=on", Timeout = 2, CommandTimeout = 3 };
        if (unavailable) { connection.Username = "ready_missing_" + Guid.NewGuid().ToString("N"); connection.Password = "readiness_secret_canary"; }
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing")
            .UseSetting("Accounts:Enabled", (accounts is not null).ToString())
            .UseSetting("Sync:Enabled", (sync is not null).ToString())
            .UseSetting("Communities:Enabled", (communities is not null).ToString())
            .UseSetting("Admin:Enabled", (admin is not null).ToString())
            .UseSetting("Web:Enabled", "false")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Timetable"] = connection.ConnectionString, ["Timetable:Schema"] = timetable,
                ["ConnectionStrings:Accounts"] = connection.ConnectionString, ["Accounts:Schema"] = accounts,
                ["Accounts:Enabled"] = (accounts is not null).ToString(), ["Sync:Schema"] = sync, ["Sync:Enabled"] = (sync is not null).ToString(),
                ["Communities:Schema"] = communities, ["Communities:Enabled"] = (communities is not null).ToString(),
                ["Admin:Schema"] = admin, ["Admin:Enabled"] = (admin is not null).ToString(),
                ["Web:Enabled"] = "false", ["Timetable:Refresh:Enabled"] = "false"
            })));
    }
    private static async Task AssertModule(HttpClient client, string module, string expected, HttpStatusCode status)
    {
        using var response = await client.GetAsync("/health/platform", Ct);
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        using var json = JsonDocument.Parse(raw);
        Assert.Equal(expected, json.RootElement.GetProperty("modules").GetProperty(module).GetString());
        Assert.DoesNotContain("readiness_secret_canary", raw);
        Assert.DoesNotContain("Password=", raw, StringComparison.OrdinalIgnoreCase);
    }
}
