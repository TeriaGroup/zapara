namespace Vograph.Desktop.Features.Schedule;

public static class ScheduleShareText
{
    public static string Format(DateTime date,
        IEnumerable<(string Start, string End, string Subject, string Room, string Teacher)> lessons)
    {
        var lines = new List<string> { $"Расписание военмех · {date:dd.MM.yyyy}" };
        var rows = lessons.ToArray();
        if (rows.Length == 0) lines.Add("Пар нет");
        else lines.AddRange(rows.Select(row =>
            $"{row.Start}–{row.End} · {row.Subject}" +
            (row.Room.Length > 0 ? " · " + row.Room : "") +
            (row.Teacher.Length > 0 ? " · " + row.Teacher : "")));
        return string.Join(Environment.NewLine, lines);
    }
}
