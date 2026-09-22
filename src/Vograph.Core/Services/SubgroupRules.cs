using System.Globalization;
using System.Text.RegularExpressions;
using Vograph.Core.Models;

namespace Vograph.Core.Services;

/// <summary>
/// A split is two or more teachers present in the same week at one bell.
/// Odd and even lessons at that bell are not a split: they never meet.
/// One weekly teacher plus one odd-week partner and one even-week partner is
/// two subgroups: the weekly teacher, and the pair that alternates.
/// </summary>
public static class SubgroupRules
{
    public sealed record Option(string Id, string Label);
    public sealed record Stream(string Id, string Title, IReadOnlyList<Option> Options, bool Joined);
    public sealed record Mark(string StreamId, IReadOnlyList<Option> Options, string? ChosenId, bool ShowChooser);
    public sealed record Member(string StreamId, IReadOnlySet<string> OptionIds, bool Joined);

    public sealed class Index
    {
        public Index(IReadOnlyList<Stream> streams, Dictionary<string, Member> membership)
        {
            Streams = streams;
            Membership = membership;
        }

        public IReadOnlyList<Stream> Streams { get; }
        private Dictionary<string, Member> Membership { get; }
        public Member? MemberOf(Lesson lesson) => Membership.TryGetValue(LessonKey(lesson), out var member) ? member : null;
    }

    public static Index Build(IReadOnlyList<Lesson> lessons)
    {
        var streams = new Dictionary<string, StreamBuilder>(StringComparer.Ordinal);
        var membership = new Dictionary<string, Member>(StringComparer.Ordinal);
        foreach (var cluster in Clusters(lessons))
        {
            if (!streams.TryGetValue(cluster.StreamId, out var builder))
            {
                builder = new StreamBuilder(cluster.StreamId, cluster.Title, cluster.SameSubject);
                streams[cluster.StreamId] = builder;
            }
            builder.Joined = builder.Joined && cluster.Lessons.Count == 1;
            foreach (var pair in cluster.OptionLabels)
                builder.Options.TryAdd(pair.Key, pair.Value);
            foreach (var lesson in cluster.Lessons)
            {
                var key = LessonKey(lesson);
                var ids = cluster.Assignment is { } assigned && assigned.TryGetValue(key, out var mapped)
                    ? mapped
                    : cluster.SameSubject
                        ? TeacherNames(lesson.TeacherRaw).Select(TeacherKey).ToHashSet(StringComparer.Ordinal)
                        : new HashSet<string>(StringComparer.Ordinal) { SlotOptionId(lesson) };
                membership.TryGetValue(key, out var previous);
                var joined = cluster.Lessons.Count == 1 && previous?.Joined != false;
                membership[key] = new Member(cluster.StreamId, ids, joined && cluster.Lessons.Count == 1);
            }
        }
        var built = streams.Values.Select(builder => builder.ToStream()).Where(stream => stream.Options.Count >= 2)
            .OrderBy(stream => stream.Id, StringComparer.Ordinal).ToList();
        var live = new HashSet<string>(built.Select(stream => stream.Id), StringComparer.Ordinal);
        var kept = membership.Where(pair => live.Contains(pair.Value.StreamId))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return new Index(built, kept);
    }

    public static List<Lesson> Visible(IReadOnlyList<Lesson> lessons, IReadOnlyDictionary<string, string> choices)
    {
        var index = Build(lessons);
        return lessons.Where(lesson => Keep(lesson, index, choices)).ToList();
    }

    public static bool Keep(Lesson lesson, Index index, IReadOnlyDictionary<string, string> choices)
    {
        var member = index.MemberOf(lesson);
        if (member is null) return true;
        var stream = index.Streams.FirstOrDefault(item => item.Id == member.StreamId);
        if (stream is null) return true;
        if (!choices.TryGetValue(stream.Id, out var choice) || stream.Options.All(option => option.Id != choice)) return true;
        if (member.Joined) return true;
        return member.OptionIds.Contains(choice);
    }

    public static Mark? MarkOf(Lesson lesson, IReadOnlyList<Lesson> day, Index index, IReadOnlyDictionary<string, string> choices)
    {
        var member = index.MemberOf(lesson);
        if (member is null) return null;
        var stream = index.Streams.FirstOrDefault(item => item.Id == member.StreamId);
        if (stream is null) return null;
        var chosen = choices.TryGetValue(stream.Id, out var raw) && stream.Options.Any(option => option.Id == raw) ? raw : null;
        var first = day.Where(item => index.MemberOf(item)?.StreamId == stream.Id)
            .OrderBy(item => TimeKey(item.TimeStart), StringComparer.Ordinal)
            .ThenBy(item => item.Index)
            .ThenBy(item => LessonKey(item), StringComparer.Ordinal)
            .FirstOrDefault();
        return new Mark(stream.Id, stream.Options, chosen, first is not null && LessonKey(first) == LessonKey(lesson));
    }

    private sealed class StreamBuilder
    {
        public StreamBuilder(string id, string title, bool sameSubject)
        {
            Id = id;
            Title = title;
            SameSubject = sameSubject;
        }

        public string Id { get; }
        public string Title { get; }
        public bool SameSubject { get; }
        public bool Joined { get; set; } = true;
        public Dictionary<string, string> Options { get; } = new(StringComparer.Ordinal);
        public Stream ToStream() => new(Id, Title, Options.Select(pair => new Option(pair.Key, pair.Value)).ToList(), Joined && SameSubject);
    }

    private sealed record Cluster(string StreamId, string Title, bool SameSubject, List<Lesson> Lessons, Dictionary<string, string> OptionLabels, Dictionary<string, HashSet<string>>? Assignment = null);

    private static List<Cluster> Clusters(IReadOnlyList<Lesson> lessons)
    {
        var found = new List<Cluster>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in lessons.GroupBy(lesson => (lesson.DayOfWeek, TimeKey(lesson.TimeStart))))
        {
            var handled = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in slot.GroupBy(SubjectKey))
            {
                var paired = PairCluster(group.ToList());
                if (paired is null) continue;
                found.Add(paired);
                foreach (var lesson in group) handled.Add(LessonKey(lesson));
            }
            var rest = slot.Where(lesson => !handled.Contains(LessonKey(lesson))).ToList();
            foreach (var week in WeekCodes(rest))
            {
                var active = rest.Where(lesson => lesson.Parity == 0 || lesson.Parity == week).ToList();
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var lesson in active.OrderBy(item => item.Index).ThenBy(item => SubjectKey(item), StringComparer.Ordinal))
                {
                    foreach (var name in TeacherNames(lesson.TeacherRaw))
                    {
                        var id = TeacherKey(name);
                        if (id.Length > 0) names.TryAdd(id, name.Trim());
                    }
                }
                if (names.Count < 2 || active.Count == 0) continue;
                var subjects = active.Select(SubjectKey).Where(key => key.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                var same = subjects.Count == 1;
                var set = string.Join("+", names.Keys.OrderBy(key => key, StringComparer.Ordinal));
                var streamId = same ? "s:" + subjects[0] + ":" + set : "t:" + slot.Key.DayOfWeek + ":" + slot.Key.Item2 + ":" + set;
                var signature = streamId + "#" + string.Join(",", active.Select(LessonKey).OrderBy(key => key, StringComparer.Ordinal));
                if (!seen.Add(signature)) continue;
                var labels = same
                    ? new Dictionary<string, string>(names, StringComparer.Ordinal)
                    : active.ToDictionary(SlotOptionId, SlotLabel, StringComparer.Ordinal);
                var title = active.OrderBy(item => item.DayOfWeek).ThenBy(item => TimeKey(item.TimeStart), StringComparer.Ordinal).ThenBy(item => item.Index).First().SubjectRaw;
                found.Add(new Cluster(streamId, title, same, active, labels));
            }
        }
        return found;
    }

    private static string SlotLabel(Lesson lesson)
    {
        var teachers = string.Join(", ", TeacherNames(lesson.TeacherRaw));
        var parts = new[] { lesson.SubjectRaw.Trim(), teachers }.Where(part => part.Length > 0);
        var label = string.Join(" · ", parts);
        return label.Length == 0 ? teachers : label;
    }

    private static string SlotOptionId(Lesson lesson)
    {
        var teachers = string.Join("+", TeacherNames(lesson.TeacherRaw).Select(TeacherKey).OrderBy(key => key, StringComparer.Ordinal));
        return SubjectKey(lesson) + "|" + teachers + "|" + lesson.ClassroomRaw.Trim();
    }

    private static string LessonKey(Lesson lesson) => string.Join("|",
        lesson.GroupId, lesson.DayOfWeek, lesson.Parity, lesson.Index,
        TimeKey(lesson.TimeStart), SubjectKey(lesson), TeacherKey(lesson.TeacherRaw), lesson.ClassroomRaw.Trim());

    private static string SubjectKey(Lesson lesson)
    {
        var norm = lesson.SubjectNormalized.Trim();
        if (norm.Length > 0) return norm;
        return lesson.SubjectRaw.Trim().ToLower(CultureInfo.GetCultureInfo("ru-RU")).Replace('ё', 'е');
    }

    internal static List<string> TeacherNames(string raw) =>
        raw.Split(';').Select(part => part.Trim()).Where(part => part.Length > 0 && part != "—" && part != "-").ToList();

    internal static string TeacherKey(string name)
    {
        var lower = name.Trim().ToLower(CultureInfo.GetCultureInfo("ru-RU")).Replace('ё', 'е');
        return Regex.Replace(lower, @"[.\s]+", " ").Trim();
    }

    internal static string TimeKey(string raw)
    {
        var match = Regex.Match(raw, @"(\d{1,2}):(\d{2})");
        if (!match.Success) return raw.Trim();
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture).ToString("00", CultureInfo.InvariantCulture) + ":" + match.Groups[2].Value;
    }

    private static Cluster? PairCluster(List<Lesson> rows)
    {
        if (rows.Count < 3 || rows.Any(lesson => lesson.Parity is < 0 or > 2) || SubjectKey(rows[0]).Length == 0) return null;
        var info = new Dictionary<string, (string Label, HashSet<int> Weeks)>(StringComparer.Ordinal);
        foreach (var lesson in rows)
        {
            var weeks = lesson.Parity == 0 ? new[] { 1, 2 } : new[] { lesson.Parity };
            foreach (var name in TeacherNames(lesson.TeacherRaw))
            {
                var id = TeacherKey(name);
                if (id.Length == 0) continue;
                if (!info.TryGetValue(id, out var current)) info[id] = current = (name.Trim(), new HashSet<int>());
                foreach (var week in weeks) current.Weeks.Add(week);
            }
        }
        var odd = info.Where(pair => pair.Value.Weeks.Contains(1)).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        var even = info.Where(pair => pair.Value.Weeks.Contains(2)).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        var stable = odd.Where(even.Contains).ToList();
        var oddOnly = odd.Where(id => !even.Contains(id)).ToList();
        var evenOnly = even.Where(id => !odd.Contains(id)).ToList();
        if (stable.Count == 0 || oddOnly.Count != 1 || evenOnly.Count != 1) return null;
        var oddKey = oddOnly[0];
        var evenKey = evenOnly[0];
        var pairId = "w:" + oddKey + "+" + evenKey;
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in stable.OrderBy(id => id, StringComparer.Ordinal)) labels[id] = info[id].Label;
        labels[pairId] = info[oddKey].Label + " · нечётная / " + info[evenKey].Label + " · чётная";
        var everyone = stable.Append(oddKey).Append(evenKey).OrderBy(id => id, StringComparer.Ordinal);
        var streamId = "s:" + SubjectKey(rows[0]) + ":" + string.Join("+", everyone);
        var assignment = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var lesson in rows)
        {
            var keys = TeacherNames(lesson.TeacherRaw).Select(TeacherKey).ToHashSet(StringComparer.Ordinal);
            var ids = keys.Where(stable.Contains).ToHashSet(StringComparer.Ordinal);
            if (keys.Contains(oddKey) || keys.Contains(evenKey)) ids.Add(pairId);
            assignment[LessonKey(lesson)] = ids;
        }
        var title = rows.OrderBy(item => item.DayOfWeek).ThenBy(item => TimeKey(item.TimeStart), StringComparer.Ordinal).ThenBy(item => item.Index).First().SubjectRaw;
        return new Cluster(streamId, title, true, rows, labels, assignment);
    }

    private static HashSet<int> WeekCodes(IEnumerable<Lesson> rows)
    {
        var codes = rows.Select(lesson => lesson.Parity).Where(parity => parity > 0).ToHashSet();
        if (codes.Count == 0) codes.Add(1);
        return codes;
    }
}
