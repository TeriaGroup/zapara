using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Xunit;
using Zapara.Server.Timetable;
using static Zapara.Server.Tests.ApiTestFactory;

namespace Zapara.Server.Tests;

public sealed partial class ApiTests
{
    [Fact]
    public async Task TT013_Database_unavailable()
    {
        using var reserved = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reserved.ExclusiveAddressUse = true;
        reserved.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)reserved.LocalEndPoint!).Port;
        var configuration = TimetableConfiguration.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Timetable"] = $"Host=127.0.0.1;Port={port};Database=zapara_test;Username=synthetic;Password=synthetic;Timeout=1",
                ["Timetable:Schema"] = "tt_api_unavailable"
            }).Build());
        await using var factory = Create(configuration, new StoreTestTimeProvider());
        using var client = factory.CreateClient();
        Assert.Equal("{\"status\":\"live\"}", await client.GetStringAsync("/health/live", Ct));
        await UnavailableAsync(client, "db_unavailable");
        output.WriteLine($"OWNED non-listening loopback port={port}; socket disposed after test; shared database untouched");
    }

    [Fact]
    public async Task TT014_No_snapshot_http()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var before = await DatabaseStateAsync(db);
        await using var factory = Create(db.Configuration, db.Clock);
        using var client = factory.CreateClient();
        Assert.Equal("{\"status\":\"live\"}", await client.GetStringAsync("/health/live", Ct));
        await UnavailableAsync(client, "snapshot_unavailable");
        Assert.Equal(before, await DatabaseStateAsync(db));
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task TT013_Uninitialized_schema_is_not_created_by_http()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, initialize: false, ct: Ct);
        await using var factory = Create(db.Configuration, db.Clock);
        using var client = factory.CreateClient();
        await UnavailableAsync(client, "db_unavailable");
        Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_class WHERE relnamespace='{db.Schema}'::regnamespace", Ct));
    }

    private static async Task UnavailableAsync(HttpClient client, string code)
    {
        foreach (var route in new[] { "/health/ready", "/api/v1/status", "/api/v1/groups", "/api/v1/groups/3313/timetable" })
            await ProblemAsync(client, route, HttpStatusCode.ServiceUnavailable, code);
        foreach (var route in new[] { "/api/v1/groups", "/api/v1/groups/opaque-unknown/timetable" })
        {
            await ProblemAsync(client, route + $"?snapshotId={Guid.NewGuid()}", HttpStatusCode.ServiceUnavailable, code);
            await InvalidPinsAsync(client, route);
        }
    }
}
