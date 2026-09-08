using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

public sealed class PostgresFixture : IAsyncDisposable
{
    private readonly Action<string> receipt;
    private bool created;
    public string Schema { get; } = "tt_test_" + Guid.NewGuid().ToString("N");
    public string QuotedSchema => $"\"{Schema}\"";
    public TimetableConfiguration Configuration { get; }
    public NpgsqlDataSource DataSource { get; }
    public StoreTestTimeProvider Clock { get; } = new();
    public SnapshotStore Store { get; }

    private PostgresFixture(Action<string> receipt)
    {
        this.receipt = receipt;
        var raw = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("ZAPARA_TEST_POSTGRES is required (no skips).");
        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (builder.Database != "zapara_test" || builder.Host is not ("127.0.0.1" or "localhost") || builder.Port != 56432)
            throw new InvalidOperationException("Only the approved local test database is allowed.");
        Configuration = TimetableConfiguration.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Timetable"] = raw, ["Timetable:Schema"] = Schema }).Build());
        DataSource = Configuration.CreateDataSource();
        Store = NewStore();
    }

    public SnapshotStore NewStore() => new(DataSource, Schema, Clock, Configuration.CreateDedicatedConnection);
    public SnapshotStore CreateStore(TimeProvider timeProvider) => new(DataSource, Schema, timeProvider, Configuration.CreateDedicatedConnection);
    public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static async Task<PostgresFixture> CreateAsync(Action<string>? receipt = null, bool initialize = true,
        CancellationToken ct = default)
    {
        var fixture = new PostgresFixture(receipt ?? Console.WriteLine);
        try
        {
            await fixture.ExecuteAsync($"CREATE SCHEMA {fixture.QuotedSchema}", ct);
            fixture.created = true;
            fixture.receipt($"CREATE {fixture.Schema}");
            if (initialize) await fixture.Store.EnsureSchemaAsync(ct);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public async Task ExecuteAsync(string sql, CancellationToken ct = default)
    {
        await using var connection = Configuration.CreateDedicatedConnection();
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<T> ScalarAsync<T>(string sql, CancellationToken ct = default)
    {
        await using var connection = Configuration.CreateDedicatedConnection();
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task ReceiptAsync(CancellationToken ct = default)
    {
        var json = await ScalarAsync<string>($"""
            SELECT json_build_object('snapshots',(SELECT count(*) FROM {QuotedSchema}.snapshots),
              'state',(SELECT json_agg(s) FROM {QuotedSchema}.state s),
              'attempts',(SELECT json_agg(a ORDER BY sequence) FROM
                (SELECT attempt_id,sequence,status,error_code FROM {QuotedSchema}.refresh_attempts) a))::text
            """, ct);
        receipt($"ROWS {Schema} {json}");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (created)
            {
                await ExecuteAsync($"DROP SCHEMA {QuotedSchema} CASCADE", CancellationToken.None);
                var remains = await ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{Schema}'", CancellationToken.None);
                receipt($"TEARDOWN {Schema} remaining={remains}");
                Assert.Equal(0, remains);
                created = false;
            }
        }
        finally { await DataSource.DisposeAsync(); }
    }
}

public sealed class StoreTestTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
