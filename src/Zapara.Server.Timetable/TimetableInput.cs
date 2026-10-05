using System.Net;
using System.Security;
using System.Xml;
using Vograph.Timetable;

namespace Zapara.Server.Timetable;

public sealed class TimetableInput(TimeProvider timeProvider)
{
    internal const int MaxBytes = 16 * 1024 * 1024;

    public async Task<SourceDocument> FromFileAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var bytes = await ReadBoundedAsync(stream, ct);
            return SourceDocument.Create(bytes, SourceKind.File, timeProvider);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
        {
            ct.ThrowIfCancellationRequested();
            throw new TimetableInputException(FailureCode.SourceRejected);
        }
    }

    // Caller owns this client. An arbitrary injected client cannot guarantee redirect/decompression policy.
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None
    }) { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<SourceDocument> FetchFixedAsync(HttpClient client, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, TimetableParser.DefaultUrl);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri != new Uri(TimetableParser.DefaultUrl)
                || response.Content.Headers.ContentEncoding.Any(value => !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                throw new TimetableInputException(FailureCode.SourceRejected);
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            var bytes = await ReadBoundedAsync(stream, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            return SourceDocument.Create(bytes, SourceKind.Http, timeProvider, response.Content.Headers.LastModified?.ToUniversalTime());
        }
        catch (OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            throw new TimetableInputException(FailureCode.SourceTimeout);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException or ArgumentException)
        {
            ct.ThrowIfCancellationRequested();
            throw new TimetableInputException(deadline.IsCancellationRequested ? FailureCode.SourceTimeout : FailureCode.SourceRejected);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxBytes - (int)output.Length + 1)), ct);
            if (count == 0) break;
            if (output.Length + count > MaxBytes) throw new TimetableInputException(FailureCode.SourceRejected);
            output.Write(buffer, 0, count);
        }
        ct.ThrowIfCancellationRequested();
        return output.ToArray();
    }

    public ValidatedSnapshot Validate(SourceDocument source)
    {
        try
        {
            var counts = TimetableXmlValidation.Validate(source);
            var (groups, lessons, start, weeks, title) = new TimetableParser().Parse(source.DecodedXml);
            TimetableXmlValidation.Require(groups.Count == counts.Groups && lessons.Count == counts.Lessons);
            var ids = groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
            var keys = new HashSet<(string, int, int, int)>();
            foreach (var lesson in lessons)
            {
                TimetableXmlValidation.Require(ids.Contains(lesson.GroupId) && lesson.DayOfWeek is >= 1 and <= 7
                    && lesson.Parity is >= 0 and <= 2 && lesson.Index > 0
                    && keys.Add((lesson.GroupId, lesson.DayOfWeek, lesson.Parity, lesson.Index)));
                TimetableXmlValidation.ValidateTime(lesson.TimeStart, lesson.Parity);
                TimetableXmlValidation.Require(TimeOnly.TryParseExact(lesson.TimeEnd, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var end)
                    && TimeOnly.ParseExact(lesson.TimeStart, "HH:mm", System.Globalization.CultureInfo.InvariantCulture).AddMinutes(95) == end);
                foreach (var field in new[] { lesson.SubjectRaw, lesson.SubjectNormalized, lesson.TeacherRaw,
                    lesson.ClassroomRaw, lesson.RoomRaw, lesson.BuildingRaw, lesson.TypeRaw })
                    TimetableXmlValidation.Require(field is null || field.Length <= 2048);
            }
            return SnapshotMapper.FromParsed(groups, lessons, start, weeks, title, source);
        }
        catch (Exception error) when (error is XmlException or ArgumentException or FormatException or OverflowException)
        {
            throw new TimetableInputException(FailureCode.SnapshotMalformed);
        }
    }
}

public sealed class TimetableInputException(FailureCode failureCode) : Exception("Источник расписания отклонён.")
{
    public FailureCode FailureCode { get; } = failureCode;
}
