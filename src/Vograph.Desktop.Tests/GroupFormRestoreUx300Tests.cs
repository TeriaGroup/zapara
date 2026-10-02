using Vograph.Desktop.Features.Groups;
using Zapara.Contracts.Communities;
using Xunit;

namespace Vograph.Desktop.Tests;

public class GroupFormRestoreUx300Tests
{
    [Fact]
    public void Restoring_sent_answer_requires_confirmation_and_preserves_cancelled_draft()
    {
        var question = new GroupFormQuestion(Guid.NewGuid(), "Ответ", "shortText", true, []);
        var answer = new GroupFormAnswer(question.QuestionId, "Отправлено", []);
        var saved = new GroupFormAnswerResponse(Guid.NewGuid(), [answer], DateTimeOffset.UtcNow);
        var form = new GroupFormResponse(Guid.NewGuid(), Guid.NewGuid(), "Анкета", "", null, false,
            [question], Guid.NewGuid(), DateTimeOffset.UtcNow, true, true, saved, 1);
        var row = new SpaceFormRow(form, true, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask);
        row.Questions[0].Text = "Черновик";

        row.RequestRestoreCommand.Execute(null);
        row.CancelRestoreCommand.Execute(null);
        Assert.Equal("Черновик", row.Questions[0].Text);

        row.RequestRestoreCommand.Execute(null);
        row.ConfirmRestoreCommand.Execute(null);
        Assert.Equal("Отправлено", row.Questions[0].Text);
        Assert.False(row.HasUnsavedAnswers);
    }

    [Fact]
    public void Single_choice_answer_can_be_cleared_before_submit()
    {
        var question = new GroupFormQuestion(Guid.NewGuid(), "Выбор", "singleChoice", true, ["А", "Б"]);
        var answer = new GroupFormAnswer(question.QuestionId, null, ["А"]);
        var row = new SpaceAnswerRow(question, answer);
        Assert.True(row.IsAnswered);

        row.ClearChoicesCommand.Execute(null);

        Assert.False(row.IsAnswered);
        Assert.True(row.IsMissing);
    }

    [Fact]
    public void Loaded_form_responses_can_hide_and_reopen_without_reloading()
    {
        var form = new GroupFormResponse(Guid.NewGuid(), Guid.NewGuid(), "Анкета", "", null, false,
            [], Guid.NewGuid(), DateTimeOffset.UtcNow, true, true, null, 1);
        var row = new SpaceFormRow(form, true, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask);
        row.Responses.Add("Участник · ответ");
        row.ShowResponses = true;

        row.ToggleResponsesCommand.Execute(null);
        Assert.False(row.ShowResponses);
        row.ToggleResponsesCommand.Execute(null);
        Assert.True(row.ShowResponses);
        Assert.Single(row.Responses);
    }
}
