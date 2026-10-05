using System.Net;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Vograph.Desktop.Features.Groups;
using Zapara.Contracts.Communities;
using Xunit;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;
namespace Vograph.Desktop.Tests;

// Actual VM handlers with fake HTTP. No views, renderer, device capture or browser.
public sealed class GroupReviewRegressionTests
{
    [AvaloniaFact] public async Task Preview_hold_reaction_and_delete_never_reach_the_server()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");var message=Guid.NewGuid();
        f.Intercept=(request,ct)=>Task.FromResult<HttpResponseMessage?>(request.Method==HttpMethod.Get&&request.RequestUri!.AbsolutePath.EndsWith("/messages",StringComparison.Ordinal)?Payload(new ChatPageResponse([new(message,f.Conversation,AccountClientTestSupport.UserId,"Аня","Текст",DateTimeOffset.UtcNow)],false)):null);
        await f.Vm.ActivateAsync();await f.Vm.PreviewPersonCommand.ExecuteAsync(null);var row=Assert.Single(f.Vm.Messages);var writes=f.Writes;
        Assert.Empty(row.HoldActions);row.Apply("reaction:like");row.Apply("delete");await f.Vm.ConfirmDeleteMessageCommand.ExecuteAsync(null);
        Assert.Equal(writes,f.Writes);Assert.False(f.Vm.HasPendingDeleteMessage);
    }
    [AvaloniaFact] public async Task Effective_moderate_exposes_and_executes_foreign_delete_without_post()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");var message=Guid.NewGuid();var deleted=false;
        f.TopicRows[0]=new(f.Topic,"Тема","user",null,null,null,0,false,canPost:false,permissions:["read","moderate"]);
        f.Intercept=(request,ct)=>
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path.EndsWith("/messages",StringComparison.Ordinal))return Task.FromResult<HttpResponseMessage?>(Payload(new ChatPageResponse([new(message,f.Conversation,f.Person,"Борис","Текст",DateTimeOffset.UtcNow)],false)));
            if(path.EndsWith("/delete",StringComparison.Ordinal)){deleted=true;return Task.FromResult<HttpResponseMessage?>(Payload(new ChatMessageResponse(message,f.Conversation,f.Person,"Борис","",DateTimeOffset.UtcNow,deleted:true)));}
            return Task.FromResult<HttpResponseMessage?>(null);
        };
        await f.Vm.ActivateAsync();var row=Assert.Single(f.Vm.Messages);Assert.False(row.Mine);Assert.Equal(["delete"],row.HoldActions);
        row.Apply("delete");await f.Vm.ConfirmDeleteMessageCommand.ExecuteAsync(null);Assert.True(deleted);Assert.True(Assert.Single(f.Vm.Messages).Deleted);
    }
    [AvaloniaFact] public async Task Poll_denial_purges_ballots_and_every_private_group_projection()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("ballots","polls");var denied=false;
        f.Intercept=(request,ct)=>
        {
            var path=request.RequestUri!.AbsolutePath;
            if(denied&&path.EndsWith("/space",StringComparison.Ordinal))return Task.FromResult<HttpResponseMessage?>(Problem(403,"forbidden"));
            if(path.EndsWith("/ballots",StringComparison.Ordinal))return Task.FromResult<HttpResponseMessage?>(Payload(new BallotBoardResponse(false,false,false,3,2,[new(Guid.NewGuid(),"Закрытый вопрос","headman","open",DateTimeOffset.UtcNow.AddDays(1),0,2,false,[new(Guid.NewGuid(),"Да",0,false),new(Guid.NewGuid(),"Нет",0,false)],"","",f.Topic)])));
            return Task.FromResult<HttpResponseMessage?>(null);
        };
        await f.Vm.ActivateAsync();Assert.Single(f.Vm.Ballots);f.Vm.AuditEvents.Add("Закрытый журнал");f.Vm.BallotQuestion="Закрытый черновик";f.Vm.SharedHomeworkBody="Закрытая домашка";
        denied=true;f.Vm.Watch(true);await Waits.Until(()=>!f.Vm.ShowBallots&&f.Vm.Ballots.Count==0,"ballot revoked",6500);
        Assert.Empty(f.Vm.Channels);Assert.Empty(f.Vm.AuditEvents);Assert.Empty(f.Vm.ArchivedChannels);Assert.Empty(f.Vm.ChannelCategories);Assert.Empty(f.Vm.BallotQuestion);Assert.Empty(f.Vm.SharedHomeworkBody);Assert.Empty(f.Vm.ChatTitle);
    }
    [AvaloniaFact] public async Task Homework_editors_are_independent_and_submit_only_the_original_snapshot()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("homework","homework");var second=Guid.NewGuid();f.TopicRows.Add(new(second,"Другая","book",null,null,null,0,true,"homework",permissions:["read","post","homework"]));
        HomeworkUpsert? submitted=null;var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Intercept=async(request,ct)=>
        {
            if(request.Method==HttpMethod.Post&&request.RequestUri!.AbsolutePath.EndsWith("/homework/share",StringComparison.Ordinal))
            {submitted=JsonSerializer.Deserialize<HomeworkUpsert>(await request.Content!.ReadAsStringAsync(ct),new JsonSerializerOptions(JsonSerializerDefaults.Web));started.TrySetResult();await release.Task.WaitAsync(ct);return Payload(new HomeworkResponse(Guid.NewGuid(),CommunityId,submitted!.Title,submitted.Body,1,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,submitted.DeadlineAt,submitted.TopicId),HttpStatusCode.Created);}
            return null;
        };
        await f.Vm.ActivateAsync();var first=f.Vm.SelectedChannel!;f.Vm.SharedHomeworkTitle="A";f.Vm.SharedHomeworkBody="Черновик A";
        var saving=f.Vm.SaveSharedHomeworkCommand.ExecuteAsync(null);await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        f.Vm.Channels.Single(x=>x.TopicId==second).OpenCommand.Execute(null);await Waits.Until(()=>f.Vm.SelectedChannel?.TopicId==second&&!f.Vm.SharedHomeworkTitle.Any(),"B editor opened");f.Vm.SharedHomeworkTitle="B";f.Vm.SharedHomeworkBody="Черновик B";
        release.TrySetResult();await saving;Assert.Equal(f.Topic,submitted!.TopicId);Assert.Equal("Черновик A",submitted.Body);Assert.Equal("Черновик B",f.Vm.SharedHomeworkBody);
        first.OpenCommand.Execute(null);await Waits.Until(()=>f.Vm.SelectedChannel?.TopicId==f.Topic,"A restored");Assert.Empty(f.Vm.SharedHomeworkTitle);
    }
    [AvaloniaFact] public async Task Unsent_answers_and_the_selected_topic_survive_navigation_and_reactivation()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("forms","forms");var second=Guid.NewGuid();f.TopicRows.Add(new(second,"Другая","user",null,null,null,0,true,permissions:["read","post"]));
        var q=new GroupFormQuestion(Guid.NewGuid(),"Вопрос","shortText",true,[]);f.FormRecords.Add(new(Guid.NewGuid(),f.Topic,"Анкета","",null,false,[q],f.Person,DateTimeOffset.UtcNow,true,true,null,0));
        await f.Vm.ActivateAsync();Assert.Single(f.Vm.Forms).Questions[0].Text="Мой несохранённый ответ";
        var formTopic=f.Vm.SelectedChannel!;f.Vm.Channels.Single(x=>x.TopicId==second).OpenCommand.Execute(null);await Waits.Until(()=>f.Vm.SelectedChannel?.TopicId==second,"other topic");
        await f.Vm.ActivateAsync();Assert.Equal(second,f.Vm.SelectedChannel!.TopicId);
        formTopic.OpenCommand.Execute(null);await Waits.Until(()=>f.Vm.Forms.Count==1,"forms returned");Assert.Equal("Мой несохранённый ответ",f.Vm.Forms[0].Questions[0].Text);
    }
    [AvaloniaFact] public async Task Response_pagination_survives_polling_and_is_complete_only_after_the_last_page()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("forms","forms");var q=new GroupFormQuestion(Guid.NewGuid(),"Вопрос","shortText",true,[]);var form=new GroupFormResponse(Guid.NewGuid(),f.Topic,"Анкета","",null,false,[q],f.Person,DateTimeOffset.UtcNow,true,true,null,3);var cursor=Guid.NewGuid();var formReads=0;
        var blocked=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GroupFormAnswerResponse Answer(string text)=>new(Guid.NewGuid(),[new(q.QuestionId,text,[])],DateTimeOffset.UtcNow);
        f.Intercept=async(request,ct)=>
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path.EndsWith("/forms",StringComparison.Ordinal)&&request.Method==HttpMethod.Get){formReads++;return Payload(new GroupFormListResponse([formReads>1?form with{OwnResponse=Answer("Сохранён на другом устройстве")}:form]));}
            if(path.EndsWith("/responses",StringComparison.Ordinal))
            {if(string.IsNullOrEmpty(request.RequestUri.Query))return Payload(new GroupFormResponsesResponse(form.FormId,[Answer("Первый"),Answer("Второй")],cursor,3));blocked.TrySetResult();await release.Task.WaitAsync(ct);return Payload(new GroupFormResponsesResponse(form.FormId,[Answer("Третий")],null,3));}
            return null;
        };
        await f.Vm.ActivateAsync();var row=Assert.Single(f.Vm.Forms);var reading=row.ResponsesCommand.ExecuteAsync(null);await blocked.Task.WaitAsync(TestContext.Current.CancellationToken);Assert.Equal(2,row.RawResponses.Count);Assert.False(row.ResponsesComplete);
        f.Vm.Watch(true);await Waits.Until(()=>f.Vm.Forms[0].Form.OwnResponse is not null,"forms polled",6500);Assert.Same(row,f.Vm.Forms[0]);Assert.Equal(2,row.Responses.Count);
        release.TrySetResult();await reading;Assert.True(row.ResponsesComplete);Assert.Equal(3,row.RawResponses.Count);
    }
    [AvaloniaFact] public async Task Incomplete_response_reads_never_open_an_export_picker()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("forms","forms");var q=new GroupFormQuestion(Guid.NewGuid(),"Вопрос","shortText",true,[]);var form=new GroupFormResponse(Guid.NewGuid(),f.Topic,"Анкета","",null,false,[q],f.Person,DateTimeOffset.UtcNow,true,true,null,2);var cursor=Guid.NewGuid();
        var picker=new FakeFileDialogs{SavePath=Path.Combine(f.Services.DataDir,"responses.csv")};f.Services.FileDialogs=picker;
        f.Intercept=(request,ct)=>Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath.EndsWith("/forms",StringComparison.Ordinal)?Payload(new GroupFormListResponse([form])):
            request.RequestUri.AbsolutePath.EndsWith("/responses",StringComparison.Ordinal)?string.IsNullOrEmpty(request.RequestUri.Query)?Payload(new GroupFormResponsesResponse(form.FormId,[new(f.Person,[new(q.QuestionId,"Часть",[])],DateTimeOffset.UtcNow)],cursor,2)):Problem(503,"server_unavailable"):null);
        await f.Vm.ActivateAsync();var row=Assert.Single(f.Vm.Forms);await row.ExportCommand.ExecuteAsync(null);Assert.False(row.ResponsesComplete);Assert.Null(picker.LastSuggestedName);Assert.False(File.Exists(picker.SavePath));
    }
    [AvaloniaFact] public async Task Schedule_topic_uses_its_own_copy_and_period_with_real_date_navigation()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("schedule","schedule","https://example.test/api/");f.Services.Db.UpsertGroup(new(){Id="my",Name="Моя",LastFetchedAt=DateTime.UtcNow});f.Services.Db.UpsertGroup(new(){Id="other",Name="О3313"});
        var settings=f.Services.Db.GetSettings();settings.MyGroupId="my";settings.PeriodStart="2026-09-14";f.Services.Db.SaveSettings(settings);
        await f.Vm.ActivateAsync();Assert.DoesNotContain("Без пар",f.Vm.ChannelSchedule);Assert.All(f.Vm.ChannelScheduleDates,x=>Assert.Contains("Нет данных",x.Label));
        f.Services.Db.InsertLesson(new(){GroupId="other",DayOfWeek=1,Parity=0,Index=1,TimeStart="09:00",TimeEnd="10:35",SubjectRaw="Предмет",SubjectNormalized="предмет",TeacherRaw="Иванов",ClassroomRaw="100"});
        f.Vm.ChannelScheduleDate=new DateTime(2026,9,7);await f.Vm.RefreshChannelScheduleCommand.ExecuteAsync(null);Assert.Contains(f.Vm.ChannelSchedule,x=>x.Contains("вне известного",StringComparison.Ordinal));
        f.Vm.ChannelTomorrowCommand.Execute(null);await f.Vm.RefreshChannelScheduleCommand.ExecuteAsync(null);Assert.Equal(DateTime.Today.AddDays(1),f.Vm.ChannelScheduleDate);Assert.Equal(7,f.Vm.ChannelScheduleDates.Count);
    }
    [AvaloniaFact] public async Task Late_schedule_projection_cannot_apply_to_a_different_community()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("schedule","schedule");var other=Guid.NewGuid();var otherTopic=Guid.NewGuid();var otherChat=Guid.NewGuid();var selectedDay=new DateTime(2026,9,28);var day=(int)selectedDay.DayOfWeek;f.Vm.ChannelScheduleDate=selectedDay;
        foreach(var (id,name) in new[]{("a","Группа А"),("b","Группа Б")}){f.Services.Db.UpsertGroup(new(){Id=id,Name=name});f.Services.Db.InsertLesson(new(){GroupId=id,DayOfWeek=day,Parity=0,Index=1,TimeStart="09:00",TimeEnd="10:35",SubjectRaw="Предмет "+id.ToUpperInvariant(),SubjectNormalized="предмет "+id,TeacherRaw="Иванов",ClassroomRaw="100"});}
        var desk=new GroupDeskResponse(false,[],[],[],[],[]);
        f.Intercept=(request,ct)=>
        {
            var path=request.RequestUri!.AbsolutePath;var isOther=path.Contains(other.ToString("D"),StringComparison.Ordinal);
            if(path.EndsWith("/communities",StringComparison.Ordinal))return Task.FromResult<HttpResponseMessage?>(Payload(new[]{new CommunityResponse(CommunityId,"Группа А","",1,"member"),new CommunityResponse(other,"Группа Б","",1,"member")}));
            if(path.EndsWith("/home",StringComparison.Ordinal))return Task.FromResult<HttpResponseMessage?>(Payload(new GroupHomeResponse(isOther?other:CommunityId,isOther?"Группа Б":"Группа А",isOther?"Группа Б":"Группа А",new(isOther?otherChat:f.Conversation,"group",isOther?other:CommunityId,"Группа",null,null,null,0),[new(AccountClientTestSupport.UserId,"anya","Аня","member",true)],[])));
            if(isOther&&path.EndsWith("/space",StringComparison.Ordinal))return Task.FromResult<HttpResponseMessage?>(Payload(new GroupSpaceResponse([new(otherTopic,"Расписание","calendar",null,null,null,0,false,"schedule",permissions:["read"])],[],new(),desk)));
            if(isOther&&path.EndsWith("/desk",StringComparison.Ordinal))return Task.FromResult<HttpResponseMessage?>(Payload(desk));
            return Task.FromResult<HttpResponseMessage?>(null);
        };
        f.Vm.RequestCommunity(CommunityId);await f.Vm.ActivateAsync();await f.Services.CoreGate.WaitAsync(TestContext.Current.CancellationToken);
        var pending=f.Vm.RefreshChannelScheduleCommand.ExecuteAsync(null);f.Vm.RequestCommunity(other);var switching=f.Vm.ActivateAsync();f.Services.CoreGate.Release();await pending;await switching;
        Assert.Equal(otherTopic,f.Vm.SelectedChannel!.TopicId);Assert.Contains(f.Vm.ChannelSchedule,x=>x.Contains("Предмет B",StringComparison.Ordinal));Assert.DoesNotContain(f.Vm.ChannelSchedule,x=>x.Contains("Предмет A",StringComparison.Ordinal));
    }
    [AvaloniaFact] public async Task A_filtered_out_archive_topic_does_not_keep_its_private_header_or_selection()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");f.ArchiveTopic();await f.Vm.ActivateAsync();await f.Vm.LoadArchiveCommand.ExecuteAsync(null);Assert.Single(f.Vm.ArchivedChannels).OpenCommand.Execute(null);
        await Waits.Until(()=>f.Vm.SelectedChannel?.Archived==true,"archive open");
        f.Intercept=(request,ct)=>Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath.EndsWith("/space/archive",StringComparison.Ordinal)?Payload(new GroupTopicListResponse([],true)):null);
        f.Vm.Watch(true);await Waits.Until(()=>f.Vm.SelectedChannel is null,"archive filtered out",6500);Assert.Empty(f.Vm.ChatTitle);Assert.Empty(f.Vm.Messages);Assert.Empty(f.Vm.Forms);Assert.Empty(f.Vm.Ballots);Assert.Empty(f.Vm.ArchivedChannels);
    }
    [AvaloniaFact] public async Task Simple_access_presets_preserve_other_powers_and_custom_rules_until_applied()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");await f.Vm.ActivateAsync();await f.Vm.LoadAccessCommand.ExecuteAsync(null);
        Assert.DoesNotContain(f.Vm.AccessRules,x=>x.Power is "joins" or "exclude" or "roles" or "grants");
        var other=f.Vm.AccessRules.First(x=>x.Power=="forms");other.State=SpaceAccessRow.States[2];
        f.Vm.SimpleReadPreset=GroupViewModel.ReadPresets[0];f.Vm.SimplePostPreset=GroupViewModel.PostPresets[2];f.Vm.ApplySimpleAccessCommand.Execute(null);
        Assert.Equal("deny",other.State.Code);Assert.Equal("allow",f.Vm.AccessRules.Single(x=>x.RoleId is null&&x.Power=="read").State.Code);
        var roleRead=f.Vm.AccessRules.First(x=>x.RoleId is not null&&x.Power=="read");roleRead.State=SpaceAccessRow.States[2];Assert.Equal("custom",f.Vm.SimpleReadPreset.Code);
        var snapshot=f.Vm.AccessRules.Select(x=>x.State.Code).ToArray();f.Vm.SimplePostPreset=GroupViewModel.PostPresets[3];f.Vm.ApplySimpleAccessCommand.Execute(null);Assert.Equal(snapshot,f.Vm.AccessRules.Select(x=>x.State.Code));
    }
}
