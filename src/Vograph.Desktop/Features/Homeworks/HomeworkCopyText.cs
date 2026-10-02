namespace Vograph.Desktop.Features.Homeworks;

public static class HomeworkCopyText
{
    public static string Format(string subject, string text, DateTime? due) =>
        subject.Trim() + Environment.NewLine + text.Trim() +
        (due is { } day ? Environment.NewLine + $"Срок: {day:dd.MM.yyyy}" : "");
}
