namespace Vograph.Desktop.Features.Groups;

public static class GroupFormDraftValidation
{
    public static string Check(string title, string description, string deadline,
        IReadOnlyList<SpaceQuestionEditor> questions)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 120)
            return "Введите название анкеты длиной до 120 символов.";
        if (description.Length > 2000) return "Описание анкеты длиннее 2000 символов.";
        try
        {
            var until = GroupViewModel.ParseDeadline(deadline);
            if (until is not null && until <= DateTimeOffset.UtcNow) return "Срок анкеты должен быть в будущем.";
        }
        catch (ArgumentException) { return "Укажите срок анкеты в формате дд.мм.гггг чч:мм."; }
        if (questions.Count is < 1 or > 30) return "Добавьте от одного до 30 вопросов.";
        for (var index = 0; index < questions.Count; index++)
        {
            var question = questions[index];
            var prefix = $"Вопрос {index + 1}: ";
            if (string.IsNullOrWhiteSpace(question.Title) || question.Title.Trim().Length > 400)
                return prefix + "введите текст до 400 символов.";
            if (!question.HasChoices) continue;
            var options = question.OptionsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (options.Length is < 2 or > 16) return prefix + "нужно от двух до 16 вариантов.";
            if (options.Any(option => option.Length > 120)) return prefix + "вариант длиннее 120 символов.";
            if (options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Length)
                return prefix + "варианты повторяются.";
        }
        return "";
    }
}
