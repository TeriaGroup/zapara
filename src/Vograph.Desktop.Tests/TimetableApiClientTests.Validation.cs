using System.Text.Json.Nodes;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public partial class TimetableApiClientTests
{
    [Theory]
    [InlineData("snapshot")]
    [InlineData("period")]
    [InlineData("count")]
    [InlineData("normalization")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("duplicate")]
    [InlineData("day")]
    [InlineData("parity")]
    [InlineData("index")]
    [InlineData("time")]
    [InlineData("overnight")]
    [InlineData("duration")]
    [InlineData("hash")]
    [InlineData("date")]
    [InlineData("zone")]
    [InlineData("weeks")]
    [InlineData("timestamp")]
    [InlineData("metaMissing")]
    public async Task Rejects_invalid_timetable(string mutation)
    {
        var schedule = Schedule();
        var lesson = schedule["lessons"]![0]!.AsObject();
        switch (mutation)
        {
            case "snapshot": schedule["meta"]!["snapshotId"] = NewPin; break;
            case "period": schedule["period"]!["title"] = "Другой"; break;
            case "count": schedule["group"]!["lessonCount"] = 2; break;
            case "normalization": lesson["subjectNormalized"] = "display name"; break;
            case "missing": lesson.Remove("subjectRaw"); break;
            case "null": lesson["subjectRaw"] = null; break;
            case "duplicate": schedule["lessons"]!.AsArray().Add(lesson.DeepClone()); break;
            case "day": lesson["dayOfWeek"] = 8; break;
            case "parity": lesson["parity"] = 3; break;
            case "index": lesson["index"] = 0; break;
            case "time": lesson["timeStart"] = "9:00"; break;
            case "overnight": lesson["timeEnd"] = "00:35"; break;
            case "duration": lesson["timeEnd"] = "11:00"; break;
            case "hash": schedule["meta"]!["sourceSha256"] = new string('A', 64); break;
            case "date": schedule["period"]!["start"] = "2026-02-30"; break;
            case "zone": schedule["period"]!["timeZone"] = "UTC"; break;
            case "weeks": schedule["period"]!["weekCount"] = 1; break;
            case "timestamp": schedule["meta"]!["fetchedAt"] = "2026-09-08T10:00:00"; break;
            case "metaMissing": schedule["meta"]!.AsObject().Remove("stale"); break;
        }
        using var handler = new FakeHttpHandler { Respond = r => Json(
            r.RequestUri!.AbsolutePath.EndsWith("/groups") ? Catalog() : schedule) };
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        Assert.Equal(TimetableApiFailure.InvalidPayload,
            (await Assert.ThrowsAsync<TimetableApiException>(() => client.FetchAsync(["a"], TestContext.Current.CancellationToken))).Failure);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("emptyId")]
    [InlineData("trim")]
    [InlineData("longId")]
    [InlineData("count")]
    [InlineData("tooMany")]
    [InlineData("total")]
    public async Task Rejects_invalid_catalog(string mutation)
    {
        var catalog = Catalog();
        var groups = catalog["groups"]!.AsArray();
        switch (mutation)
        {
            case "duplicate": groups.Add(groups[0]!.DeepClone()); break;
            case "emptyId": groups[0]!["id"] = ""; break;
            case "trim": groups[0]!["id"] = " a "; break;
            case "longId": groups[0]!["id"] = new string('a', 65); break;
            case "count": groups[0]!["lessonCount"] = -1; break;
            case "tooMany": for (int i = 0; i < 5000; i++) groups.Add(Group("g" + i)); break;
            case "total": groups[0]!["lessonCount"] = 50001; break;
        }
        using var handler = new FakeHttpHandler { Respond = _ => Json(catalog) };
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        Assert.Equal(TimetableApiFailure.InvalidPayload,
            (await Assert.ThrowsAsync<TimetableApiException>(() => client.FetchAsync([], TestContext.Current.CancellationToken))).Failure);
    }

    [Theory]
    [InlineData("<Timetable/>")]
    [InlineData("<html>secret</html>")]
    [InlineData("{\"groups\":")]
    [InlineData("null")]
    public async Task Rejects_non_contract_json(string body)
    {
        using var handler = new FakeHttpHandler { Respond = _ => Text(body) };
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        await Assert.ThrowsAsync<TimetableApiException>(() => client.FetchAsync([], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejects_duplicate_properties_but_accepts_unknown_metadata_extensions()
    {
        var catalog = Catalog();
        catalog["meta"]!["future"] = new JsonObject { ["version"] = 3 };
        using var handler = new FakeHttpHandler { Respond = _ => Json(catalog) };
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        Assert.Equal(2, (await client.FetchAsync([], TestContext.Current.CancellationToken)).Groups.Length);
        handler.Respond = _ => Text(catalog.ToJsonString().Replace("\"weekCount\":2", "\"weekCount\":2,\"weekCount\":2"));
        await Assert.ThrowsAsync<TimetableApiException>(() => client.FetchAsync([], TestContext.Current.CancellationToken));
    }
}
