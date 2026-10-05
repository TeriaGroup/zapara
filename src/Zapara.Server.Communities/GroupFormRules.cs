using Zapara.Contracts.Communities;
namespace Zapara.Server.Communities;
public static class GroupFormRules
{
    public static void Validate(GroupFormRequest request, DateTimeOffset now)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Title) || !Text(request.Title,120,false) || !Text(request.Description,2000,true)
            || request.DeadlineAt is { } end && (end <= now || end.Offset != TimeSpan.Zero) || request.Questions is null || request.Questions.Count is < 1 or > 30
            || request.Questions.Any(q => q is null) || request.Questions.Select(q => q.QuestionId).Distinct().Count() != request.Questions.Count) throw new ArgumentException("Некорректная анкета.");
        foreach (var q in request.Questions)
        {
            if (q.QuestionId == Guid.Empty || string.IsNullOrWhiteSpace(q.Title) || !Text(q.Title,400,false) || q.Options is null
                || q.Kind is not ("shortText" or "longText" or "singleChoice" or "multipleChoice")) throw new ArgumentException("Некорректная анкета.");
            if (q.Kind is "singleChoice" or "multipleChoice")
            {
                if (q.Options.Count is < 2 or > 16 || q.Options.Any(v => string.IsNullOrWhiteSpace(v) || !Text(v,120,false))
                    || q.Options.Distinct(StringComparer.Ordinal).Count() != q.Options.Count) throw new ArgumentException("Некорректная анкета.");
            }
            else if (q.Options.Count != 0) throw new ArgumentException("Некорректная анкета.");
        }
    }
    public static void ValidateAnswers(IReadOnlyList<GroupFormQuestion> questions, IReadOnlyList<GroupFormAnswer> answers)
    {
        if (answers is null || answers.Any(a => a is null || a.Choices is null) || answers.Select(a => a.QuestionId).Distinct().Count() != answers.Count
            || answers.Any(a => !questions.Any(q => q.QuestionId == a.QuestionId))) throw new ArgumentException("Некорректная анкета.");
        foreach (var q in questions)
        {
            var a = answers.SingleOrDefault(a => a.QuestionId == q.QuestionId);
            if (a is null) { if (q.Required) throw new ArgumentException("Некорректная анкета."); continue; }
            if (q.Kind is "shortText" or "longText")
            {
                if (a.Choices.Count != 0 || !Text(a.Text??"",q.Kind == "shortText" ? 500 : 8000,true)
                    || q.Required && string.IsNullOrWhiteSpace(a.Text)) throw new ArgumentException("Некорректная анкета.");
            }
            else if (!string.IsNullOrEmpty(a.Text) || a.Choices.Distinct().Count() != a.Choices.Count || a.Choices.Any(c => !q.Options.Contains(c))
                || q.Kind == "singleChoice" && a.Choices.Count > 1 || q.Required && a.Choices.Count == 0) throw new ArgumentException("Некорректная анкета.");
        }
    }
    private static bool Text(string? value,int maximum,bool multiline)
        => value is not null && value.Length<=maximum && !value.Any(c=>char.IsControl(c)&&!(multiline&&c is '\n' or '\r' or '\t'));
}
