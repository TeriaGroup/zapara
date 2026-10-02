namespace Vograph.Desktop.Features.Groups;

public static class BallotDraftValidation
{
    public static string PreviewDeadline(string days, DateTimeOffset now) =>
        int.TryParse(days, out var count) && count is >= 1 and <= 14
            ? $"Закроется примерно {now.AddDays(count):dd.MM.yyyy HH:mm}" : "";
    public static string Check(string question, IReadOnlyList<string> options, string days)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Trim().Length > 400)
            return "Введите вопрос голосования длиной до 400 символов.";
        if (options.Count < 2 || string.IsNullOrWhiteSpace(options[0]) || string.IsNullOrWhiteSpace(options[1]))
            return "Заполните первые два варианта ответа.";
        var chosen = options.Select(option => option.Trim()).Where(option => option.Length > 0).ToArray();
        if (chosen.Length is < 2 or > 6) return "Заполните от двух до шести вариантов ответа.";
        if (chosen.Any(option => option.Length > 80)) return "Каждый вариант должен быть не длиннее 80 символов.";
        if (chosen.Distinct(StringComparer.OrdinalIgnoreCase).Count() != chosen.Length)
            return "Варианты повторяются. Измените повторяющийся текст.";
        if (!int.TryParse(days, out var count) || count is < 1 or > 14)
            return "Укажите срок голосования от 1 до 14 дней.";
        return "";
    }
}
