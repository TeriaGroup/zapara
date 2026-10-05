using Vograph.Desktop.Features.Groups;
using Xunit;

namespace Vograph.Desktop.Tests;

public class BallotDraftValidationUx300Tests
{
    [Fact]
    public void Invalid_question_duplicate_options_and_deadline_get_specific_messages()
    {
        Assert.Contains("вопрос", BallotDraftValidation.Check("", ["А", "Б"], "3"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("повтор", BallotDraftValidation.Check("Выбор", ["А", "а"], "3"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("первые два", BallotDraftValidation.Check("Выбор", ["", "А", "Б"], "3"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("срок", BallotDraftValidation.Check("Выбор", ["А", "Б"], "15"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("", BallotDraftValidation.Check("Выбор", ["А", "Б"], "3"));
    }

    [Fact]
    public void Deadline_preview_uses_selected_days_without_sending_vote()
    {
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(3));

        Assert.Equal("Закроется примерно 04.10.2026 12:00", BallotDraftValidation.PreviewDeadline("3", now));
        Assert.Equal("", BallotDraftValidation.PreviewDeadline("15", now));
    }
}
