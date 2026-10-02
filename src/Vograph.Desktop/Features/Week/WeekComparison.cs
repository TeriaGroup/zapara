namespace Vograph.Desktop.Features.Week;

public sealed record WeekSlotChange(string Kind, int Weekday, WeekRow Row);

public static class WeekComparison
{
    private readonly record struct SlotKey(int Weekday, string Start, string End, string Subject,
        string Type, string Teacher, string Classroom);

    public static IReadOnlyList<WeekSlotChange> Compare(IEnumerable<WeekDay> current, IEnumerable<WeekDay> other)
    {
        static SlotKey Key(WeekDay day, WeekRow row) => new(day.Dow, row.Time, row.TimeEnd,
            row.SubjectRaw, row.TypeRaw, row.Teacher, row.ClassroomRaw);
        var old = current.SelectMany(day => day.Rows.Select(row => (Key: Key(day, row), Day: day, Row: row))).ToArray();
        var next = other.SelectMany(day => day.Rows.Select(row => (Key: Key(day, row), Day: day, Row: row))).ToArray();
        var remaining = next.GroupBy(entry => entry.Key).ToDictionary(group => group.Key, group => group.Count());
        var changed = new List<WeekSlotChange>();
        foreach (var entry in old)
        {
            if (remaining.TryGetValue(entry.Key, out var count) && count > 0) remaining[entry.Key] = count - 1;
            else changed.Add(new("Убрано", entry.Day.Dow, entry.Row));
        }
        remaining = old.GroupBy(entry => entry.Key).ToDictionary(group => group.Key, group => group.Count());
        foreach (var entry in next)
        {
            if (remaining.TryGetValue(entry.Key, out var count) && count > 0) remaining[entry.Key] = count - 1;
            else changed.Add(new("Добавлено", entry.Day.Dow, entry.Row));
        }
        return changed.OrderBy(change => change.Weekday).ThenBy(change => change.Row.Time)
            .ThenBy(change => change.Kind).ToArray();
    }
}
