using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vograph.Core.Models;
using Vograph.Timetable;

namespace Zapara.Server.Timetable;

public sealed class JsonTimetableInput(TimeProvider clock)
{
    private const int MaxDocumentBytes = 512 * 1024;
    private const int MaxGroups = 1000;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public async Task<ValidatedSnapshot> FetchAsync(HttpClient client, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        var token = linked.Token;
        long totalBytes = 0;
        try
        {
            var metaJson = await FetchDocumentAsync(VoenmehScheduleClient.MetaUrl);
            var meta = ValidateMeta(metaJson);
            using var concurrency = new SemaphoreSlim(6);
            var fetched = await Task.WhenAll(meta.Groups.Select(async name =>
            {
                await concurrency.WaitAsync(token);
                try
                {
                    var json = await FetchDocumentAsync($"{VoenmehScheduleClient.LessonsUrl}?name={Uri.EscapeDataString(name)}&type=group");
                    return (Name: name, Json: json, Lessons: ValidateLessons(json, name));
                }
                finally { concurrency.Release(); }
            }));
            var after = await FetchDocumentAsync(VoenmehScheduleClient.MetaUrl);
            // The API has no snapshot token. Reject a refresh crossing a published metadata generation.
            Require(SHA256.HashData(Utf8.GetBytes(metaJson)).AsSpan().SequenceEqual(SHA256.HashData(Utf8.GetBytes(after))));
            Require(fetched.Sum(group => group.Lessons.Count) > 0);
            var parsed = VoenmehScheduleParser.Assemble(meta, fetched.Select(g => (g.Name, g.Lessons)));
            var archive = JsonSerializer.SerializeToUtf8Bytes(new
            {
                format = "voenmeh-json-v1", meta = Element(metaJson),
                groups = fetched.Select(g => new { name = g.Name, data = Element(g.Json) })
            });
            Require(archive.Length <= TimetableInput.MaxBytes);
            DateTimeOffset? modified = DateTimeOffset.TryParse(meta.UpdatedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var updated) ? updated.ToUniversalTime() : null;
            return SnapshotMapper.FromParsed(parsed.Groups, parsed.Lessons, parsed.PeriodStart, parsed.WeekCount,
                parsed.PeriodTitle, SourceDocument.FromJsonArchive(archive, clock, modified));
        }
        catch (OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            throw new TimetableInputException(FailureCode.SourceTimeout);
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            ct.ThrowIfCancellationRequested();
            throw new TimetableInputException(FailureCode.SourceRejected);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            ct.ThrowIfCancellationRequested();
            throw new TimetableInputException(FailureCode.SnapshotMalformed);
        }

        async Task<string> FetchDocumentAsync(string url)
        {
            using var requestDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), clock);
            using var requestToken = CancellationTokenSource.CreateLinkedTokenSource(token, requestDeadline.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken.Token);
            if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri != new Uri(url)
                || response.Content.Headers.ContentEncoding.Any(x => !x.Equals("identity", StringComparison.OrdinalIgnoreCase))
                || response.Content.Headers.ContentLength > MaxDocumentBytes)
                throw new TimetableInputException(FailureCode.SourceRejected);
            await using var stream = await response.Content.ReadAsStreamAsync(requestToken.Token);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var count = await stream.ReadAsync(buffer, requestToken.Token);
                if (count == 0) break;
                if (output.Length + count > MaxDocumentBytes || Interlocked.Add(ref totalBytes, count) > TimetableInput.MaxBytes)
                    throw new TimetableInputException(FailureCode.SourceRejected);
                output.Write(buffer, 0, count);
            }
            return Utf8.GetString(output.ToArray()).TrimStart('\uFEFF');
        }
    }

    private static VoenmehScheduleMeta ValidateMeta(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Require(root.ValueKind == JsonValueKind.Object && root.TryGetProperty("groups", out _));
        var groups = root.GetProperty("groups");
        Require(groups.ValueKind == JsonValueKind.Array && groups.GetArrayLength() is > 0 and <= MaxGroups);
        Require(groups.EnumerateArray().All(g => g.ValueKind == JsonValueKind.String && g.GetString() is { Length: > 0 and <= 64 } text && text == text.Trim()));
        Require(groups.EnumerateArray().Select(g => g.GetString()).Distinct(StringComparer.Ordinal).Count() == groups.GetArrayLength());
        var meta = VoenmehScheduleParser.ParseMeta(json);
        Require(meta.PeriodTitle.Length <= 2048 && Regex.IsMatch(meta.PeriodTitle, @"20\d{2}"));
        return meta;
    }

    private static List<Lesson> ValidateLessons(string json, string group)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Require(root.ValueKind == JsonValueKind.Object && root.TryGetProperty("lessons", out _));
        var rows = root.GetProperty("lessons");
        Require(rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() <= 500);
        foreach (var row in rows.EnumerateArray())
        {
            Require(row.ValueKind == JsonValueKind.Object);
            foreach (var field in new[] { "time", "week", "kind", "subject" })
                Require(row.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 2048);
            Require(row.GetProperty("subject").GetString()!.Trim().Length > 0);
            Require(row.GetProperty("week").GetString()!.ToLowerInvariant() is "" or "both" or "all" or "odd" or "even" or "нечетная" or "нечётная" or "четная" or "чётная");
            Require(TimeOnly.TryParseExact(row.GetProperty("time").GetString(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
            foreach (var field in new[] { "teachers", "rooms" })
                Require(row.TryGetProperty(field, out var values) && values.ValueKind == JsonValueKind.Array
                    && values.GetArrayLength() <= 50 && values.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String && v.GetString()!.Length <= 256));
        }
        var lessons = VoenmehScheduleParser.ParseLessons(json, group);
        Require(lessons.Count == rows.GetArrayLength());
        foreach (var lesson in lessons)
        {
            Require(lesson.DayOfWeek is >= 1 and <= 7 && lesson.Parity is >= 0 and <= 2 && lesson.Index > 0);
            foreach (var value in new[] { lesson.SubjectRaw, lesson.TeacherRaw, lesson.ClassroomRaw }) Require(value.Length <= 2048);
        }
        return lessons;
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new TimetableInputException(FailureCode.SnapshotMalformed);
    }

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
