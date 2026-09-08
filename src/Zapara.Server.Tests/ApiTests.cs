using System.Net;
using System.Text.Json;
using Xunit;
using Zapara.Server.Timetable;
using static Zapara.Server.Tests.ApiTestFactory;

namespace Zapara.Server.Tests;

public sealed partial class ApiTests(ITestOutputHelper output)
{
    [Fact]
    public async Task TT010_Stale_and_pins()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var start = db.Clock.Now;
        var a = await PublishAsync(db);
        await using var factory = Create(db.Configuration, db.Clock);
        using var client = factory.CreateClient();
        db.Clock.Now = start.AddHours(24).AddTicks(-1);
        await AssertStateAsync(client, a, false);
        db.Clock.Now = start.AddHours(24);
        await AssertStateAsync(client, a, true);
        db.Clock.Now = start;
        await using (var lease = await db.Store.TryAcquireAsync(Ct))
        {
            Assert.NotNull(lease);
            await db.Store.RecordFailedAttemptAsync(lease, FailureCode.SourceTimeout, Ct);
        }
        await AssertStateAsync(client, a, true);
        var failed = await GetAsync(client, "/api/v1/status");
        Assert.Equal("source_timeout", failed.GetProperty("refresh").GetProperty("lastFailureCode").GetString());
        Assert.Equal(failed.GetProperty("refresh").GetProperty("lastSuccessAt").GetString(),
            failed.GetProperty("refresh").GetProperty("lastFailureAt").GetString());
        var b = await PublishAsync(db, TestSnapshotFactory.Create(db.Clock, smaller: true));
        await AssertStateAsync(client, b, false);
        var current = await GetAsync(client, $"/api/v1/groups?snapshotId={b}");
        Assert.False(current.GetProperty("meta").GetProperty("stale").GetBoolean());
        Assert.Single(current.GetProperty("groups").EnumerateArray());
        var old = await GetAsync(client, $"/api/v1/groups?snapshotId={a}");
        Assert.True(old.GetProperty("meta").GetProperty("stale").GetBoolean());
        Assert.Equal(2, old.GetProperty("groups").GetArrayLength());
        var schedule = await GetAsync(client, $"/api/v1/groups/3313/timetable?snapshotId={a}");
        Assert.Equal(a, schedule.GetProperty("meta").GetProperty("snapshotId").GetGuid());
        Assert.Equal("лек Математика", schedule.GetProperty("lessons")[0].GetProperty("subjectRaw").GetString());
        Assert.Equal(old.GetProperty("refresh").GetRawText(), current.GetProperty("refresh").GetRawText());
        Assert.Equal("source_timeout", current.GetProperty("refresh").GetProperty("lastFailureCode").GetString());
        Assert.Empty((await GetAsync(client, $"/api/v1/groups/9999/timetable?snapshotId={a}")).GetProperty("lessons").EnumerateArray());
        await ProblemAsync(client, "/api/v1/groups/9999/timetable", HttpStatusCode.NotFound, "group_not_found");
        await db.ReceiptAsync(Ct);
    }

    private static async Task AssertStateAsync(HttpClient client, Guid id, bool stale)
    {
        Assert.Equal("ready", (await GetAsync(client, "/health/ready")).GetProperty("status").GetString());
        string? refresh = null;
        foreach (var route in new[] { "/api/v1/status", "/api/v1/groups", "/api/v1/groups/3313/timetable" })
        {
            var json = await GetAsync(client, route);
            Assert.Equal(id, json.GetProperty("meta").GetProperty("snapshotId").GetGuid());
            Assert.Equal(stale, json.GetProperty("meta").GetProperty("stale").GetBoolean());
            refresh ??= json.GetProperty("refresh").GetRawText();
            Assert.Equal(refresh, json.GetProperty("refresh").GetRawText());
        }
    }

    [Theory]
    [InlineData(SourceKind.File)]
    [InlineData(SourceKind.Http)]
    public async Task TT011_Wire_and_routes(SourceKind kind)
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var fixture = TestSnapshotFactory.Create(db.Clock, kind: kind);
        var id = await PublishAsync(db, fixture);
        var before = await DatabaseStateAsync(db);
        await using var factory = Create(db.Configuration, db.Clock);
        using var client = factory.CreateClient();
        var groups = await GetAsync(client, "/api/v1/groups");
        Keys(groups, "period", "meta", "refresh", "groups");
        Keys(groups.GetProperty("period"), "start", "weekCount", "title", "timeZone");
        Assert.Equal("2026-09-01", groups.GetProperty("period").GetProperty("start").GetString());
        Assert.Equal("Europe/Moscow", groups.GetProperty("period").GetProperty("timeZone").GetString());
        var wire = groups.Deserialize<GroupsResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(fixture.Period, wire.Period);
        Assert.Equal(fixture.Groups.OrderBy(g => g.Name, StringComparer.Ordinal).ThenBy(g => g.Id, StringComparer.Ordinal), wire.Groups);
        Assert.Equal(id, wire.Meta.SnapshotId);
        Assert.Equal(fixture.Source.FetchedAtUtc, wire.Meta.FetchedAt);
        Assert.Equal(db.Clock.Now, wire.Meta.PublishedAt);
        Assert.Equal(fixture.Source.SourceModifiedAt, wire.Meta.SourceModifiedAt);
        Assert.Equal(kind == SourceKind.File ? "file" : "http", wire.Meta.SourceKind);
        Assert.Equal(fixture.Source.SourceUrl, wire.Meta.SourceUrl);
        Assert.Equal(fixture.Source.SourceSha256, wire.Meta.SourceSha256);
        Assert.False(wire.Meta.Stale);
        var status = await GetAsync(client, "/api/v1/status");
        Keys(status, "meta", "refresh");
        Assert.Equal(groups.GetProperty("meta").GetRawText(), status.GetProperty("meta").GetRawText());
        Keys(status.GetProperty("meta"), "snapshotId", "fetchedAt", "publishedAt", "sourceModifiedAt", "sourceKind", "sourceUrl", "sourceSha256", "stale");
        Keys(status.GetProperty("refresh"), "lastAttemptId", "lastAttemptStatus", "lastSuccessAt", "lastFailureAt", "lastFailureCode", "abandoned");
        Assert.NotNull(wire.Refresh.LastAttemptId);
        Assert.Equal("success", wire.Refresh.LastAttemptStatus);
        Assert.Equal(db.Clock.Now, wire.Refresh.LastSuccessAt);
        Assert.Null(wire.Refresh.LastFailureAt);
        Assert.Null(wire.Refresh.LastFailureCode);
        Assert.False(wire.Refresh.Abandoned);
        var schedule = await GetAsync(client, $"/api/v1/groups/3313/timetable?snapshotId={id}");
        Keys(schedule, "period", "meta", "refresh", "group", "lessons");
        Keys(schedule.GetProperty("group"), "id", "name", "lessonCount");
        Keys(schedule.GetProperty("lessons")[0], "dayOfWeek", "parity", "index", "timeStart", "timeEnd", "subjectRaw", "subjectNormalized",
            "typeRaw", "teacherRaw", "classroomRaw", "roomRaw", "buildingRaw");
        var timetable = schedule.Deserialize<TimetableResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(fixture.Lessons.Select(l => l.Value), timetable.Lessons);
        Assert.Equal(wire.Meta, timetable.Meta);
        Assert.Equal(wire.Refresh, timetable.Refresh);
        Assert.Equal(wire.Period, timetable.Period);
        Assert.Equal(fixture.Groups.Single(g => g.Id == "3313"), timetable.Group);
        var empty = await GetAsync(client, "/api/v1/groups/9999/timetable");
        Assert.Equal(0, empty.GetProperty("group").GetProperty("lessonCount").GetInt32());
        Assert.Empty(empty.GetProperty("lessons").EnumerateArray());
        await ProblemAsync(client, "/api/v1/groups/opaque-unknown/timetable", HttpStatusCode.NotFound, "group_not_found");
        foreach (var route in new[] { "/api/v1/groups", "/api/v1/groups/3313/timetable" })
        {
            await ProblemAsync(client, route + $"?snapshotId={Guid.NewGuid()}", HttpStatusCode.NotFound, "snapshot_not_found");
            await InvalidPinsAsync(client, route);
        }
        foreach (var route in new[] { "/api/v1/ingest", "/api/v1/refresh", "/ingest", "/refresh" })
        {
            using var response = await client.PostAsync(route, null, Ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        foreach (var route in new[] { "/api/v1/status", "/api/v1/groups", "/api/v1/groups/3313/timetable" })
        {
            using var response = await client.PostAsync(route, null, Ct);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
        Assert.Equal(before, await DatabaseStateAsync(db));
        await db.ReceiptAsync(Ct);
    }

    private static async Task InvalidPinsAsync(HttpClient client, string route)
    {
        foreach (var query in new[] { "?snapshotId=", "?snapshotId", "?snapshotId=bad-token", "?snapshotId=%20",
            $"?snapshotId={Guid.Empty}&snapshotId={Guid.Empty}", $"?snapshotId=&snapshotId={Guid.Empty}" })
            await ProblemAsync(client, route + query, HttpStatusCode.BadRequest, "invalid_snapshot_id");
    }
}
