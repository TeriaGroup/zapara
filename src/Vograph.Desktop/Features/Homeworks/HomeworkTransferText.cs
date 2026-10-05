namespace Vograph.Desktop.Features.Homeworks;

public sealed record HomeworkTransferItem(string Subject, string Text, DateTime? Due, bool Done,
    IReadOnlyList<string> FileNames);

public static class HomeworkTransferText
{
    public static string Format(IReadOnlyList<HomeworkTransferItem> items)
    {
        if (items.Count == 0) return "В текущем списке нет личных заданий.";
        var lines = new List<string> { $"Домашние задания · Расписание военмех · {items.Count}" };
        foreach (var item in items)
        {
            lines.Add($"{item.Subject} · {(item.Due is { } due ? $"срок {due:dd.MM.yyyy}" : "без срока")}" +
                (item.Done ? " · готово у меня" : ""));
            lines.Add(item.Text);
            if (item.FileNames.Count > 0) lines.Add("Файлы: " + string.Join(", ", item.FileNames));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
