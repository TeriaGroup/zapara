using Npgsql;
using Vograph.Timetable;
using Xunit;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

public sealed class TimetableReadinessCompatibilityTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Last_good_supported_schema_is_ready_without_an_upgrade(int version)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await PostgresFixture.CreateAsync(initialize: version == 3, ct: ct);
        if (version == 2)
        {
            await Apply("001_timetable.sql");
            await Apply("002_json_source.sql");
        }
        await ApiTestFactory.PublishAsync(db);
        var before = await ApiTestFactory.DatabaseStateAsync(db);
        var options = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES"))
        { Options = "-c default_transaction_read_only=on" };
        await using var data = NpgsqlDataSource.Create(options.ConnectionString);
        var store = new SnapshotStore(data, db.Schema, db.Clock, db.Configuration.CreateDedicatedConnection);
        Assert.True(await store.IsReadyAsync(ct));
        Assert.Equal(version, await db.ScalarAsync<int>($"SELECT version FROM {db.QuotedSchema}.schema_version", ct));
        Assert.Equal(before, await ApiTestFactory.DatabaseStateAsync(db));

        async Task Apply(string name)
        {
            using var stream = typeof(SnapshotStore).Assembly.GetManifestResourceStream("Zapara.Server.Timetable.Sql." + name)!;
            using var reader = new StreamReader(stream);
            await db.ExecuteAsync((await reader.ReadToEndAsync(ct)).Replace("{{schema}}", db.QuotedSchema)
                .Replace("{{url}}", "'" + TimetableParser.DefaultUrl + "'")
                .Replace("{{xml_url}}", "'" + TimetableParser.DefaultUrl + "'")
                .Replace("{{json_url}}", "'" + VoenmehScheduleClient.LegacyMetaUrl + "'"), ct);
        }
    }
}
