using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Vograph.Core.Models;
using static Vograph.Core.Services.TimetableApiJson;

namespace Vograph.Core.Services;

internal static class TimetableApiReader
{
    internal static TimetableApiSnapshot Catalog(JsonElement root, CancellationToken ct)
    {
        Object(root);
        var period = Period(Field(root, "period"));
        var meta = Meta(Field(root, "meta"));
        var refresh = Refresh(Field(root, "refresh"));
        var groups = ImmutableArray.CreateBuilder<TimetableApiGroup>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var item in Array(root, "groups", 5000).EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var group = Group(item);
            Require(ids.Add(group.Id));
            count += group.LessonCount;
            Require(count <= 50000);
            groups.Add(group);
        }
        return new(period, meta, refresh, groups.ToImmutable(),
            ImmutableDictionary<string, TimetableApiDownloadedGroup>.Empty.WithComparers(StringComparer.Ordinal));
    }

    internal static TimetableApiDownloadedGroup Timetable(JsonElement root, TimetableApiSnapshot catalog,
        TimetableApiGroup expected, CancellationToken ct)
    {
        Object(root);
        Require(Period(Field(root, "period")) == catalog.Period);
        var meta = Meta(Field(root, "meta"));
        // Staleness/refresh can change during a read; immutable generation identity cannot.
        Require((meta with { Stale = catalog.Meta.Stale }) == catalog.Meta);
        var refresh = Refresh(Field(root, "refresh"));
        var group = Group(Field(root, "group"));
        Require(group == expected);
        var array = Array(root, "lessons", 50000);
        Require(array.GetArrayLength() == group.LessonCount);
        var lessons = ImmutableArray.CreateBuilder<TimetableApiLesson>();
        var keys = new HashSet<(int, int, int)>();
        foreach (var item in array.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var lesson = Lesson(item);
            Require(keys.Add((lesson.DayOfWeek, lesson.Parity, lesson.Index)));
            lessons.Add(lesson);
        }
        return new(group, meta, refresh, lessons.ToImmutable());
    }

    private static TimetableApiPeriod Period(JsonElement obj)
    {
        Object(obj);
        Require(DateOnly.TryParseExact(Text(obj, "start", 10), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var start));
        var weeks = Integer(obj, "weekCount");
        var zone = Text(obj, "timeZone", 64);
        Require(weeks == 2 && zone == "Europe/Moscow");
        return new(start, weeks, Text(obj, "title", 256, true), zone);
    }

    private static TimetableApiMeta Meta(JsonElement obj)
    {
        Object(obj);
        var hash = Text(obj, "sourceSha256", 64);
        Require(hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'));
        return new(Uuid(obj, "snapshotId"), Timestamp(obj, "fetchedAt"), Timestamp(obj, "publishedAt"),
            NullableTimestamp(obj, "sourceModifiedAt"), Text(obj, "sourceKind", 64, true),
            NullableText(obj, "sourceUrl"), hash, Boolean(obj, "stale"));
    }

    private static TimetableApiRefresh Refresh(JsonElement obj)
    {
        Object(obj);
        var id = Field(obj, "lastAttemptId").ValueKind == JsonValueKind.Null
            ? (Guid?)null : Uuid(obj, "lastAttemptId");
        return new(id, NullableText(obj, "lastAttemptStatus"), NullableTimestamp(obj, "lastSuccessAt"),
            NullableTimestamp(obj, "lastFailureAt"), NullableText(obj, "lastFailureCode"), Boolean(obj, "abandoned"));
    }

    private static TimetableApiGroup Group(JsonElement obj)
    {
        Object(obj);
        var id = Text(obj, "id", 64, true);
        Require(ValidId(id));
        var count = Integer(obj, "lessonCount");
        Require(count is >= 0 and <= 50000);
        return new(id, Text(obj, "name", 128, true), count);
    }

    private static TimetableApiLesson Lesson(JsonElement obj)
    {
        Object(obj);
        var day = Integer(obj, "dayOfWeek");
        var parity = Integer(obj, "parity");
        var index = Integer(obj, "index");
        Require(day is >= 1 and <= 7 && parity is >= 0 and <= 2 && index > 0);
        var start = Time(obj, "timeStart");
        var end = Time(obj, "timeEnd");
        Require(end > start && end - start == TimeSpan.FromMinutes(95));
        var raw = Text(obj, "subjectRaw", 2048, true);
        var normalized = Text(obj, "subjectNormalized", 2048, true);
        Require(normalized == ParityService.NormalizeSubject(raw));
        return new(day, parity, index, Text(obj, "timeStart", 5), Text(obj, "timeEnd", 5), raw, normalized,
            NullableText(obj, "typeRaw"), NullableText(obj, "teacherRaw"), NullableText(obj, "classroomRaw"),
            NullableText(obj, "roomRaw"), NullableText(obj, "buildingRaw"));
    }
}
