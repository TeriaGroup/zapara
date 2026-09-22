using System.Net;
using Vograph.Timetable;
using Xunit;
using Zapara.Server.Timetable;
using static Zapara.Server.Tests.JsonTimetableInputTests;

namespace Zapara.Server.Tests;

public sealed class PreferredTimetableIngestTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Json_failure_uses_validated_XML_or_preserves_last_good(bool badXml)
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var service = new IngestService(db.Store, new TimetableInput(db.Clock));
        Assert.Equal(0, await service.IngestFileAsync(PostgresFixture.FixturePath("valid-b.xml"), Ct));
        var before = (await db.Store.ReadCurrentAsync(Ct))!.Meta.SnapshotId;
        var xmlCalls = 0;
        using var http = new HttpClient(new InputHandler(async (request, _) =>
        {
            if (request.RequestUri!.AbsoluteUri != TimetableParser.DefaultUrl)
                return Response(request, request.RequestUri.AbsoluteUri == VoenmehScheduleClient.MetaUrl ? Meta : "{}", HttpStatusCode.OK);
            Interlocked.Increment(ref xmlCalls);
            return Response(request, await File.ReadAllTextAsync(PostgresFixture.FixturePath(badXml ? "invalid.xml" : "valid-a.xml"), Ct));
        }));
        var result = await service.IngestRefreshResultAsync(http, db.Clock, Ct);
        Assert.Equal(badXml ? 2 : 0, result.ExitCode);
        Assert.Equal(1, xmlCalls);
        var after = Assert.IsType<SnapshotRead>(await db.Store.ReadCurrentAsync(Ct));
        if (badXml)
        {
            Assert.Equal(before, after.Meta.SnapshotId);
            Assert.True(after.Meta.Stale);
        }
        else
        {
            Assert.NotEqual(before, after.Meta.SnapshotId);
            Assert.Equal(TimetableParser.DefaultUrl, after.Meta.SourceUrl);
        }
        Assert.Equal(2L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.refresh_attempts", Ct));
    }

    [Fact]
    public async Task Complete_json_publishes_once_and_busy_lease_never_requests_upstream()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var calls = 0;
        using var http = new HttpClient(new InputHandler((request, _) =>
        {
            Interlocked.Increment(ref calls);
            Assert.NotEqual(TimetableParser.DefaultUrl, request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Response(request, request.RequestUri.AbsoluteUri == VoenmehScheduleClient.MetaUrl ? Meta : Lessons));
        }));
        var service = new IngestService(db.Store, new TimetableInput(db.Clock));
        Assert.Equal(0, (await service.IngestRefreshResultAsync(http, db.Clock, Ct)).ExitCode);
        var count = calls;
        await using var held = await db.Store.TryAcquireAsync(Ct);
        Assert.Equal(3, (await service.IngestRefreshResultAsync(http, db.Clock, Ct)).ExitCode);
        Assert.Equal(count, calls);
    }
}
