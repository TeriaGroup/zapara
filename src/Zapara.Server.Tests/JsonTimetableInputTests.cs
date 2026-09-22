using System.Net;
using System.Text;
using System.Text.Json;
using Vograph.Timetable;
using Xunit;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

public sealed class JsonTimetableInputTests
{
    internal const string Meta = """{"has_data":true,"updated_at":"2026-09-21T00:00:00Z","period":"ОСЕННИЙ СЕМЕСТР 2026/2027","groups":["А863С","09С33"]}""";
    internal const string Lessons = """{"lessons":[{"day":1,"time":"9:00","week":"odd","kind":"лек","subject":"Математика","teachers":["Барт Е.Л."],"rooms":["493"]}]}""";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    internal static HttpResponseMessage Response(HttpRequestMessage request, string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        RequestMessage = request, Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task All_groups_are_fetched_with_fixed_encoded_urls_and_empty_group_is_preserved()
    {
        var calls = new List<string>();
        using var http = new HttpClient(new InputHandler((request, _) =>
        {
            lock (calls) calls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Response(request, request.RequestUri!.AbsoluteUri == VoenmehScheduleClient.MetaUrl ? Meta
                : request.RequestUri.Query.Contains("09") ? "{\"lessons\":[]}" : Lessons));
        }));
        var snapshot = await new JsonTimetableInput(TimeProvider.System).FetchAsync(http, Ct);
        Assert.Equal(2, snapshot.Groups.Length);
        Assert.Equal(0, snapshot.Groups.Single(g => g.Id == "09С33").LessonCount);
        Assert.Equal("09:00", Assert.Single(snapshot.Lessons).Value.TimeStart);
        Assert.Equal(VoenmehScheduleClient.MetaUrl, snapshot.Source.SourceUrl);
        Assert.Equal(SourceKind.Http, snapshot.Source.SourceKind);
        Assert.Equal(2, calls.Count(url => url == VoenmehScheduleClient.MetaUrl));
        Assert.Contains(VoenmehScheduleClient.LessonsUrl + "?name=" + Uri.EscapeDataString("А863С") + "&type=group", calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"lessons\":[{\"day\":99}]}")]
    [InlineData("{\"lessons\":[]}")]
    [InlineData("<html>gateway error</html>")]
    public async Task Missing_malformed_or_wholly_empty_snapshot_cannot_publish(string payload)
    {
        using var http = new HttpClient(new InputHandler((request, _) => Task.FromResult(Response(request,
            request.RequestUri!.AbsoluteUri == VoenmehScheduleClient.MetaUrl ? Meta : payload))));
        await Assert.ThrowsAsync<TimetableInputException>(() => new JsonTimetableInput(TimeProvider.System).FetchAsync(http, Ct));
    }

    [Fact]
    public async Task Metadata_change_during_group_fetch_rejects_mixed_generation()
    {
        var metaCalls = 0;
        using var http = new HttpClient(new InputHandler((request, _) => Task.FromResult(Response(request,
            request.RequestUri!.AbsoluteUri == VoenmehScheduleClient.MetaUrl
                ? Interlocked.Increment(ref metaCalls) == 1 ? Meta : Meta.Replace("00:00:00", "00:01:00") : Lessons))));
        await Assert.ThrowsAsync<TimetableInputException>(() => new JsonTimetableInput(TimeProvider.System).FetchAsync(http, Ct));
    }

    [Theory]
    [InlineData(302)]
    [InlineData(404)]
    [InlineData(503)]
    public async Task Failed_group_response_rejects_whole_snapshot(int status)
    {
        using var http = new HttpClient(new InputHandler((request, _) => Task.FromResult(Response(request,
            request.RequestUri!.AbsoluteUri == VoenmehScheduleClient.MetaUrl ? Meta : Lessons,
            request.RequestUri.AbsoluteUri == VoenmehScheduleClient.MetaUrl ? HttpStatusCode.OK : (HttpStatusCode)status))));
        await Assert.ThrowsAsync<TimetableInputException>(() => new JsonTimetableInput(TimeProvider.System).FetchAsync(http, Ct));
    }

    [Theory]
    [InlineData("redirected")]
    [InlineData("compressed")]
    [InlineData("oversized")]
    public async Task Transport_rejects_changed_origin_encoding_and_oversized_response(string mode)
    {
        using var http = new HttpClient(new InputHandler((request, _) =>
        {
            var response = Response(request, mode == "oversized" ? new string(' ', 512 * 1024 + 1) : Meta);
            if (mode == "redirected") response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1/private");
            if (mode == "compressed") response.Content.Headers.ContentEncoding.Add("gzip");
            return Task.FromResult(response);
        }));
        var failure = await Assert.ThrowsAsync<TimetableInputException>(() => new JsonTimetableInput(TimeProvider.System).FetchAsync(http, Ct));
        Assert.Equal(FailureCode.SourceRejected, failure.FailureCode);
    }

    [Fact]
    public async Task Aggregate_budget_bounds_all_group_responses_together()
    {
        var meta = JsonSerializer.Serialize(new { has_data = true, period = "ОСЕННИЙ 2026", groups = Enumerable.Range(1, 35).Select(i => "group" + i) });
        var large = new string(' ', 500000) + Lessons;
        using var http = new HttpClient(new InputHandler((request, _) => Task.FromResult(Response(request,
            request.RequestUri!.AbsoluteUri == VoenmehScheduleClient.MetaUrl ? meta : large))));
        var failure = await Assert.ThrowsAsync<TimetableInputException>(() => new JsonTimetableInput(TimeProvider.System).FetchAsync(http, Ct));
        Assert.Equal(FailureCode.SourceRejected, failure.FailureCode);
    }

    [Fact]
    public async Task Request_deadline_is_distinct_from_caller_cancellation()
    {
        var clock = new InputClock();
        using var http = new HttpClient(new InputHandler((_, token) =>
        {
            clock.Expire();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException();
        }));
        var failure = await Assert.ThrowsAsync<TimetableInputException>(() => new JsonTimetableInput(clock).FetchAsync(http, Ct));
        Assert.Equal(FailureCode.SourceTimeout, failure.FailureCode);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new JsonTimetableInput(clock).FetchAsync(http, cancelled.Token));
    }
}
