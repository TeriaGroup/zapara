using Vograph.Desktop.Features.Groups;
using Xunit;

namespace Vograph.Desktop.Tests;

public class GroupFormDraftValidationUx300Tests
{
    [Fact]
    public void Invalid_choice_question_identifies_its_number_and_repeat()
    {
        var first = new SpaceQuestionEditor { Title = "Первый" };
        var second = new SpaceQuestionEditor
        { Title = "Второй", Kind = SpaceQuestionEditor.Kinds[2], OptionsText = "А\nА" };

        var error = GroupFormDraftValidation.Check("Анкета", "", "", [first, second]);

        Assert.Contains("Вопрос 2", error);
        Assert.Contains("повтор", error, StringComparison.OrdinalIgnoreCase);
    }
}
