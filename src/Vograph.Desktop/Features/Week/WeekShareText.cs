namespace Vograph.Desktop.Features.Week;

public static class WeekShareText
{
    public static string Format(IEnumerable<WeekDay> days)
    {
        var lines = new List<string> { "Расписание военмех" };
        foreach (var day in days)
        {
            lines.Add($"{day.Title}, {day.Date:dd.MM.yyyy}");
            if (day.Rows.Count == 0) { lines.Add("Пар нет"); continue; }
            lines.AddRange(day.Rows.Select(row =>
                $"{row.Time}{(row.TimeEnd.Length > 0 ? "–" + row.TimeEnd : "")} · {row.Name}" +
                (row.Room.Length > 0 ? " · " + row.Room : "")));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
