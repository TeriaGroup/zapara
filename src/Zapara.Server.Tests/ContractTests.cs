using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vograph.Timetable;
using Zapara.Server.Timetable;
using Xunit;

namespace Zapara.Server.Tests;

public class ContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Source_owns_bytes_hash_and_utc_provenance()
    {
        var bytes = Encoding.UTF8.GetBytes(Fixture("valid-a.xml"));
        var original = bytes.ToArray();
        var source = SourceDocument.Create(bytes, SourceKind.File, new Clock());
        bytes[0] = 0;
        var exported = source.Bytes.ToArray();
        exported[1] = 0;
        Assert.Equal(original, source.Bytes.ToArray());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(), source.SourceSha256);
        Assert.Equal(TimetableParser.DecodeXml(original), source.DecodedXml);
        Assert.Equal(Now, source.FetchedAtUtc);
        Assert.Null(source.SourceUrl);
        Assert.Null(source.SourceModifiedAt);
        var http = SourceDocument.Create(original, SourceKind.Http, new Clock(), Now.ToOffset(TimeSpan.FromHours(3)));
        Assert.Equal(TimetableParser.DefaultUrl, http.SourceUrl);
        Assert.Equal(TimeSpan.Zero, http.SourceModifiedAt!.Value.Offset);
        Assert.Throws<ArgumentException>(() => SourceDocument.Create(original, SourceKind.File, new Clock(), Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceDocument.Create(original, (SourceKind)99, new Clock()));
    }

    [Fact]
    public void Mapper_detaches_mutable_parser_models_and_preserves_empty_groups()
    {
        var source = SourceDocument.Create(Encoding.UTF8.GetBytes(Fixture("valid-a.xml")), SourceKind.File, new Clock());
        var (groups, lessons, start, weeks, title) = new TimetableParser().Parse(source.DecodedXml);
        var snapshot = SnapshotMapper.FromParsed(groups, lessons, start, weeks, title, source);
        groups[0].Name = "changed";
        lessons[0].SubjectRaw = "changed";
        groups.Clear();
        lessons.Clear();
        Assert.Equal("А863С", snapshot.Groups[0].Name);
        Assert.Equal(0, snapshot.Groups.Single(group => group.Id == "9999").LessonCount);
        Assert.Equal("лек Математика", snapshot.Lessons[0].Value.SubjectRaw);
        Assert.Equal("493;", snapshot.Lessons[0].Value.ClassroomRaw);
        Assert.Equal(new DateOnly(2026, 9, 1), snapshot.Period.Start);
        Assert.Equal("Europe/Moscow", snapshot.Period.TimeZone);
        var array = snapshot.Groups.ToArray();
        var copy = new ValidatedSnapshot(snapshot.Period, array, snapshot.Lessons, source);
        array[0] = new GroupDto("changed", "changed", 99);
        Assert.Equal("3313", copy.Groups[0].Id);
        Assert.Same(source, copy.Source);
    }

    [Fact]
    public void Storage_and_wire_have_exact_keys_and_no_source_or_native_id()
    {
        var source = SourceDocument.Create(Encoding.UTF8.GetBytes(Fixture("valid-a.xml")), SourceKind.File, new Clock());
        var (groups, lessons, start, weeks, title) = new TimetableParser().Parse(source.DecodedXml);
        var snapshot = SnapshotMapper.FromParsed(groups, lessons, start, weeks, title, source);
        var refresh = new RefreshDto(null, null, null, null, null, false);
        var meta = new SnapshotMetaDto(Guid.Empty, Now, Now, null, "file", null, source.SourceSha256, false);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(snapshot, options));
        Assert.Equal(new[] { "period", "groups", "lessons" }, payload.RootElement.EnumerateObject().Select(p => p.Name));
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(new TimetableResponse(snapshot.Period, meta, refresh, snapshot.Groups[0], [snapshot.Lessons[0].Value]), options));
        var root = wire.RootElement;
        Assert.Equal(new[] { "period", "meta", "refresh", "group", "lessons" }, root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("2026-09-01", root.GetProperty("period").GetProperty("start").GetString());
        Assert.Equal(new[] { "snapshotId", "fetchedAt", "publishedAt", "sourceModifiedAt", "sourceKind", "sourceUrl", "sourceSha256", "stale" }, root.GetProperty("meta").EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "lastAttemptId", "lastAttemptStatus", "lastSuccessAt", "lastFailureAt", "lastFailureCode", "abandoned" }, root.GetProperty("refresh").EnumerateObject().Select(p => p.Name));
        Assert.False(root.GetProperty("lessons")[0].TryGetProperty("id", out _));
        var roundtrip = JsonSerializer.Deserialize<SnapshotPayload>(JsonSerializer.Serialize(snapshot.Payload, options), options)!;
        Assert.Equal(snapshot.Lessons[0], roundtrip.Lessons[0]);
        var selection = new SnapshotSelection(null, null, refresh);
        Assert.Null(selection.CurrentId);
    }

    [Fact]
    public void Failure_codes_are_closed_and_roundtrip()
    {
        var expected = new[] { "snapshot_malformed", "source_rejected", "source_timeout", "db_unavailable", "publication_unknown", "cancelled", "abandoned" };
        Assert.Equal(expected, Enum.GetValues<FailureCode>().Select(code => code.ToStorageCode()));
        foreach (var code in Enum.GetValues<FailureCode>())
            Assert.Equal(code, FailureCodes.Parse(code.ToStorageCode()));
        Assert.Throws<ArgumentException>(() => FailureCodes.Parse("raw exception password"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((FailureCode)99).ToStorageCode());
    }

    [Fact]
    public void Fixtures_are_distinct_and_invalid_lacks_period()
    {
        var (groups, lessons, _, _, _) = new TimetableParser().Parse(Fixture("valid-b.xml"));
        Assert.Single(groups);
        Assert.Equal("пр Физика", Assert.Single(lessons).SubjectRaw);
        Assert.Null(System.Xml.Linq.XDocument.Parse(Fixture("invalid.xml")).Root!.Element("Period"));
    }
}
