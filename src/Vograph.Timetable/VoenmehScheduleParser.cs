using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Group = Vograph.Core.Models.Group;

namespace Vograph.Timetable;

public sealed record VoenmehScheduleMeta(
    DateTime PeriodStart,
    int WeekCount,
    string PeriodTitle,
    IReadOnlyList<string> Groups,
    string? UpdatedAt);

public sealed record ParsedSchedule(
    IReadOnlyList<Group> Groups,
    IReadOnlyList<Lesson> Lessons,
    DateTime PeriodStart,
    int WeekCount,
    string PeriodTitle,
    string? UpdatedAt = null,
    IReadOnlyList<string>? FetchedGroupNames = null);

public static class VoenmehScheduleParser
{
    public static VoenmehScheduleMeta ParseMeta(string json)
    {
        var root = PayloadObject(json);
        if (!Bool(root, "has_data"))
            throw new InvalidOperationException("Расписание пока не загружено на сайте университета");
        var title = Str(root, "period");
        var groups = Strings(root, "groups");
        if (groups.Count == 0)
            throw new InvalidOperationException("Сайт университета не отдал список групп");
        return new VoenmehScheduleMeta(
            PeriodStart(title),
            2,
            string.IsNullOrWhiteSpace(title) ? "Расписание" : title,
            groups,
            BlankToNull(Str(root, "updated_at")));
    }

    public static List<Lesson> ParseLessons(string json, string groupId)
    {
        var root = PayloadObject(json);
        var parsed = new List<Lesson>();
        if (!root.TryGetProperty("lessons", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return NumberByTime(parsed);
        foreach (var item in rows.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var day = Int(item, "day");
            if (day is < 1 or > 7) continue;
            var week = Str(item, "week").ToLowerInvariant();
            var parity = week switch
            {
                "odd" or "нечетная" or "нечётная" => 1,
                "even" or "четная" or "чётная" => 2,
                _ => 0
            };
            var kind = Str(item, "kind").Trim();
            var subject = Str(item, "subject").Trim();
            var discRaw = kind.Length > 0 && subject.Length > 0 ? $"{kind} {subject}"
                : subject.Length > 0 ? subject : kind;
            var timeStart = PadTime(Str(item, "time"));
            var timeEnd = "";
            if (TimeSpan.TryParse(timeStart, CultureInfo.InvariantCulture, out var ts))
                timeEnd = ts.Add(TimeSpan.FromMinutes(95)).ToString(@"hh\:mm");
            var teachers = Strings(item, "teachers");
            var rooms = Strings(item, "rooms");
            var classroomRaw = rooms.Count == 0 ? "" : string.Join("; ", rooms);
            if (classroomRaw.Length > 0 && !classroomRaw.EndsWith(';')) classroomRaw += ";";
            var (roomRaw, buildingRaw) = TimetableParser.PlaceOf(classroomRaw);
            parsed.Add(new Lesson
            {
                GroupId = groupId,
                DayOfWeek = day,
                Parity = parity,
                Index = 0,
                TimeStart = timeStart,
                TimeEnd = timeEnd,
                SubjectRaw = discRaw,
                SubjectNormalized = ParityService.NormalizeSubject(discRaw),
                TeacherRaw = string.Join("; ", teachers),
                RoomRaw = roomRaw,
                BuildingRaw = buildingRaw,
                TypeRaw = kind,
                ClassroomRaw = classroomRaw
            });
        }
        return NumberByTime(parsed);
    }

    public static ParsedSchedule Assemble(VoenmehScheduleMeta meta, IEnumerable<(string Name, List<Lesson> Lessons)> loaded)
    {
        var byName = loaded.ToDictionary(x => x.Name, x => x.Lessons, StringComparer.Ordinal);
        var groups = meta.Groups.Select(name => new Group
        {
            Id = name,
            Name = name,
            Url = VoenmehScheduleClient.Origin
        }).ToList();
        var lessons = new List<Lesson>();
        foreach (var g in groups)
            if (byName.TryGetValue(g.Id, out var list)) lessons.AddRange(list);
        return new ParsedSchedule(groups, lessons, meta.PeriodStart, meta.WeekCount, meta.PeriodTitle, meta.UpdatedAt,
            loaded.Select(x => x.Name).ToList());
    }

    internal static List<Lesson> NumberByTime(List<Lesson> lessons)
    {
        foreach (var group in lessons.GroupBy(l => (l.DayOfWeek, l.Parity)))
        {
            var ordered = group.OrderBy(l => l.TimeStart, StringComparer.Ordinal).ThenBy(l => l.SubjectRaw, StringComparer.Ordinal).ToList();
            for (var i = 0; i < ordered.Count; i++) ordered[i].Index = i + 1;
        }
        return lessons;
    }

    internal static DateTime PeriodStart(string title)
    {
        var years = Regex.Matches(title, @"20\d{2}").Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        var spring = title.Contains("весен", StringComparison.OrdinalIgnoreCase);
        var year = spring ? years.LastOrDefault(2026) : years.FirstOrDefault(2026);
        return spring ? new DateTime(year, 2, 9) : new DateTime(year, 9, 1);
    }

    private static string PadTime(string raw)
    {
        var m = Regex.Match(raw.Trim(), @"(\d{1,2}):(\d{2})");
        if (!m.Success) return "";
        return $"{int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture):00}:{m.Groups[2].Value}";
    }

    private static JsonElement PayloadObject(string json)
    {
        var trimmed = json.TrimStart('\uFEFF', ' ', '\n', '\r', '\t');
        if (TimetableParser.IsHtml(trimmed) || trimmed.StartsWith('<'))
            throw new InvalidOperationException(TimetableParser.NotTimetable);
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException(TimetableParser.NotTimetable);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(TimetableParser.NotTimetable);
        }
    }

    private static List<string> Strings(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        return arr.EnumerateArray()
            .Where(i => i.ValueKind == JsonValueKind.String)
            .Select(i => i.GetString()?.Trim() ?? "")
            .Where(s => s.Length > 0)
            .ToList();
    }

    private static string Str(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return "";
        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }

    private static int Int(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), CultureInfo.InvariantCulture, out n)) return n;
        return 0;
    }

    private static bool Bool(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return false;
        return v.ValueKind == JsonValueKind.True;
    }

    private static string? BlankToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
