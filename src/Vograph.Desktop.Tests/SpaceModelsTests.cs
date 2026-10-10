using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Features.Groups;
using Vograph.Desktop.Features.Schedule;
using Zapara.Contracts.Communities;
using Xunit;
namespace Vograph.Desktop.Tests;

public sealed class SpaceModelsTests
{
    [Theory]
    [InlineData("chat","chat",true)]
    [InlineData("chat","subject",true)]
    [InlineData("chat","announcements",true)]
    [InlineData("materials","materials",true)]
    [InlineData("forms","forms",false)]
    [InlineData("homework","homework",false)]
    [InlineData("schedule","schedule",false)]
    [InlineData("ballots","polls",false)]
    [InlineData("futureKind","futureTemplate",false)]
    public void Composer_is_only_for_supported_message_topics(string kind,string template,bool expected)
    {
        var topic=new GroupTopicResponse(Guid.NewGuid(),"Тема","user",null,null,null,0,true,kind,template:template,permissions:["read","post","media"]);
        var row=new GroupChannelRow(topic,new RelayCommand(()=>{}));
        Assert.Equal(expected,row.CanPost);
        if(kind=="futureKind")Assert.False(row.Supported);
    }
    [Fact] public void Single_choice_switches_instead_of_collecting_multiple_options()
    {
        var question=new GroupFormQuestion(Guid.NewGuid(),"Выбор","singleChoice",true,["А","Б","В"]);
        var row=new SpaceAnswerRow(question,new(question.QuestionId,null,["А"]));
        row.Options[1].Selected=true;
        Assert.Equal(["Б"],row.Answer().Choices);
        Assert.Null(row.Answer().Text);
    }
    [Fact] public void Multiple_choice_restores_own_response_and_keeps_all_selections()
    {
        var question=new GroupFormQuestion(Guid.NewGuid(),"Выбор","multipleChoice",true,["А","Б","В"]);
        var row=new SpaceAnswerRow(question,new(question.QuestionId,null,["А","В"]));row.Options[1].Selected=true;
        Assert.Equal(["А","Б","В"],row.Answer().Choices);
    }
    [Fact] public void Required_form_marks_the_first_missing_answer_and_keeps_the_draft()
    {
        var first = new GroupFormQuestion(Guid.NewGuid(), "Первый", "shortText", true, []);
        var second = new GroupFormQuestion(Guid.NewGuid(), "Второй", "singleChoice", true, ["А", "Б"]);
        var form = new GroupFormResponse(Guid.NewGuid(), Guid.NewGuid(), "Анкета", "", null, false,
            [first, second], Guid.NewGuid(), DateTimeOffset.UtcNow, true, true, null, 0);
        static Task NoOp(SpaceFormRow _) => Task.CompletedTask;
        var row = new SpaceFormRow(form, true, NoOp, NoOp, NoOp);
        Assert.True(row.MarkFirstMissingRequired());
        Assert.Equal(first.QuestionId, row.FirstMissingId);
        Assert.Equal("Обязательные ответы: 0 из 2", row.RequiredProgressText);
        row.Questions[0].Text = "Ответ";
        Assert.Equal("Обязательные ответы: 1 из 2", row.RequiredProgressText);
        Assert.False(row.HasMissingRequired);
        Assert.True(row.MarkFirstMissingRequired());
        Assert.Equal(second.QuestionId, row.FirstMissingId);
        row.Questions[1].Options[0].Selected = true;
        Assert.Equal("Обязательные ответы: 2 из 2", row.RequiredProgressText);
        Assert.False(row.HasMissingRequired);
        Assert.Equal("Ответ", row.Questions[0].Text);
        Assert.False(row.MarkFirstMissingRequired());
    }
    [Fact] public void Choice_question_focus_skips_the_hidden_text_input()
    {
        var row = new Avalonia.Controls.StackPanel();
        var hiddenText = new Avalonia.Controls.TextBox { IsVisible = false };
        var choice = new Avalonia.Controls.CheckBox { IsVisible = true };
        row.Children.Add(hiddenText); row.Children.Add(choice);
        Assert.Same(choice, GroupSpecializedView.FirstVisibleAnswerInput(row));
    }
    [Fact] public void Published_form_refresh_preserves_unsent_input_when_own_response_has_not_changed()
    {
        var question=new GroupFormQuestion(Guid.NewGuid(),"Текст","longText",false,[]);
        var form=new GroupFormResponse(Guid.NewGuid(),Guid.NewGuid(),"Анкета","",null,false,[question],Guid.NewGuid(),DateTimeOffset.UtcNow,true,true,null,0);
        static Task NoOp(SpaceFormRow row)=>Task.CompletedTask;
        var old=new SpaceFormRow(form,true,NoOp,NoOp,NoOp);old.Questions[0].Text="Неотправленный ответ";
        var next=new SpaceFormRow(form with {ResponseCount=1},true,NoOp,NoOp,NoOp);next.CopyUnsavedAnswers(old);
        Assert.Equal("Неотправленный ответ",next.Questions[0].Text);
        Assert.False(new SpaceFormRow(form,false,NoOp,NoOp,NoOp).CanRespond);
    }
    [Fact] public void Deadline_parser_has_explicit_format_and_supports_no_deadline()
    {
        Assert.Null(GroupViewModel.ParseDeadline(""));
        Assert.Equal(new DateTime(2026,9,30,18,0,0),GroupViewModel.ParseDeadline("30.09.2026 18:00")!.Value.LocalDateTime);
        Assert.Throws<ArgumentException>(()=>GroupViewModel.ParseDeadline("31.02.2026 18:00"));
        Assert.Throws<ArgumentException>(()=>GroupViewModel.ParseDeadline("вечером"));
    }
    [Fact] public async Task Deadline_toggle_retains_row_identity_and_immediately_updates_completion()
    {
        var row=new PlannerDeadlineRow("local:1",()=>Task.CompletedTask,r=>{r.Done=!r.Done;return Task.CompletedTask;},()=>{});
        row.Update("Задание","Предмет",new DateTime(2026,9,30),false,false);
        await row.ToggleCommand.ExecuteAsync(null);Assert.True(row.Done);Assert.Equal("Выполнено",row.CompletionCaption);
        await row.ToggleCommand.ExecuteAsync(null);Assert.False(row.Done);Assert.Contains("Задание",row.CompletionCaption);
    }
    [Fact] public void Question_editor_deduplicates_options_and_never_adds_options_to_text()
    {
        var editor=new SpaceQuestionEditor{Title="Выберите",Kind=SpaceQuestionEditor.Kinds[2],OptionsText=" А \nБ\nА\n"};
        Assert.Equal(["А","Б"],editor.Question().Options);
        editor.Kind=SpaceQuestionEditor.Kinds[0];Assert.Empty(editor.Question().Options);
        editor.Kind=SpaceQuestionEditor.Kinds[3];Assert.Equal(["А","Б"],editor.Question().Options);
        Assert.Equal(editor.Id,editor.Question().QuestionId);
    }
}
