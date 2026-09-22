using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Vograph.Timetable;
using Xunit;
using Zapara.Server.Timetable;
using static Zapara.Server.Tests.JsonTimetableInputTests;

namespace Zapara.Server.Tests;

public sealed class PublicRefreshTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Refresh_cli_publishes_JSON_and_preserves_busy_and_failed_exit_codes()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var fail = false;
        using var http = new HttpClient(new InputHandler((request, _) => Task.FromResult(Response(request,
            fail ? "<html>error</html>" : request.RequestUri!.AbsoluteUri == VoenmehScheduleClient.MetaUrl ? Meta : Lessons))));
        var service = new IngestService(db.Store, new TimetableInput(db.Clock));
        Assert.Equal(0, await Zapara.Ingest.Program.RunAsync(["ingest", "--refresh"], db.Store, service, http, Ct));
        var first = (await db.Store.ReadCurrentAsync(Ct))!;
        Assert.Equal(VoenmehScheduleClient.MetaUrl, first.Meta.SourceUrl);
        await using (var held = await db.Store.TryAcquireAsync(Ct))
            Assert.Equal(3, await Zapara.Ingest.Program.RunAsync(["ingest", "--refresh"], db.Store, service, http, Ct));
        fail = true;
        Assert.Equal(2, await Zapara.Ingest.Program.RunAsync(["ingest", "--refresh"], db.Store, service, http, Ct));
        Assert.Equal(first.Meta.SnapshotId, (await db.Store.ReadCurrentAsync(Ct))!.Meta.SnapshotId);
    }

    [Fact]
    public async Task Packaged_teacher_response_truthfully_reports_fallback_without_claiming_a_fetch()
    {
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing").ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Accounts:Enabled"] = "false", ["Sync:Enabled"] = "false", ["Communities:Enabled"] = "false",
                ["Admin:Enabled"] = "false", ["Web:Enabled"] = "false", ["Timetable:Refresh:Enabled"] = "false"
            })));
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/api/v1/teachers", Ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.True(json.RootElement.TryGetProperty("metadata", out var metadata));
        Assert.Equal("packaged", metadata.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("fetchedAt").ValueKind);
        Assert.Equal("memory", metadata.GetProperty("cacheLifetime").GetString());
    }
}
