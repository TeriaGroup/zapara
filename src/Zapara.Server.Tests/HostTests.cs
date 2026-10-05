using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Zapara.Server.Tests;

public class HostTests
{
    [Fact]
    public async Task Liveness_is_real_without_database_and_data_errors_are_safe()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Timetable"] = null, ["Timetable:Schema"] = null })));
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal("{\"status\":\"live\"}", await client.GetStringAsync("/health/live", ct));
        using var ingest = await client.PostAsync("/api/v1/ingest", null, ct);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, ingest.StatusCode);
        await ApiTestFactory.ProblemAsync(client, "/api/v1/status", System.Net.HttpStatusCode.ServiceUnavailable, "db_unavailable");
    }
}
