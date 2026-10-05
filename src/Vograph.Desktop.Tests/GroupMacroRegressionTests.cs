using Avalonia.Headless.XUnit;
using Vograph.Desktop.Features.Groups;
using Vograph.Desktop.Shell;
using Zapara.Contracts.Communities;
using Xunit;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;
namespace Vograph.Desktop.Tests;
public sealed class GroupMacroRegressionTests
{
    [AvaloniaFact] public async Task Visible_archive_is_refreshed_while_general_is_selected_and_late_reply_cannot_resurrect_it()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");var archived=Guid.NewGuid();var arc=new GroupTopicResponse(archived,"Закрытая","user",null,null,null,0,false,archived:true,canPost:false,permissions:["read"]);f.TopicRows.Add(arc);
        await f.Vm.ActivateAsync();await f.Vm.LoadArchiveCommand.ExecuteAsync(null);var stale=Assert.Single(f.Vm.ArchivedChannels);Assert.Equal(f.Topic,f.Vm.SelectedChannel!.TopicId);
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var requests=0;
        f.Intercept=async(request,ct)=>
        {
            if(request.RequestUri!.AbsolutePath.EndsWith("/space/archive",StringComparison.Ordinal))
            {if(++requests==1){started.TrySetResult();await release.Task.WaitAsync(ct);return Payload(new GroupTopicListResponse([arc],true));}return Payload(new GroupTopicListResponse([],true));}
            return null;
        };
        var old=f.Vm.LoadArchiveCommand.ExecuteAsync(null);await started.Task.WaitAsync(TestContext.Current.CancellationToken);f.Vm.Watch(true);
        await Waits.Until(()=>requests>=2&&f.Vm.ArchivedChannels.Count==0,"archive updated beside general",6500);release.TrySetResult();await old;
        Assert.Empty(f.Vm.ArchivedChannels);Assert.Equal(f.Topic,f.Vm.SelectedChannel!.TopicId);var reads=f.MessageReads;stale.OpenCommand.Execute(null);Assert.Equal(reads,f.MessageReads);Assert.Equal(f.Topic,f.Vm.SelectedChannel.TopicId);
    }
    private const string Subject="пр ПРЕДМЕТ";
    private static (Guid Homework,Guid Task) SeedB(GroupSpaceViewModelTests.Fixture f)
    {
        var homework=Guid.NewGuid();var task=Guid.NewGuid();f.TopicRows[0]=new(f.Topic,"Предмет B","book",null,null,null,0,true,template:"subject",subject:Subject,permissions:["read","post","access"]);
        f.TopicRows.Add(new(homework,"Задания B","book",null,null,null,0,true,"homework",subject:Subject,permissions:["read","post","homework"]));
        f.Services.Db.UpsertGroup(new(){Id="a",Name="Личная A"});f.Services.Db.UpsertGroup(new(){Id="b",Name="О3313"});var settings=f.Services.Db.GetSettings();settings.MyGroupId="a";f.Services.Db.SaveSettings(settings);
        var lessonDate=DateTime.Today.AddDays(1);if(lessonDate.DayOfWeek==DayOfWeek.Sunday)lessonDate=lessonDate.AddDays(1);var day=(int)lessonDate.DayOfWeek;
        foreach(var (id,teacher,room) in new[]{("a","Учитель A","100"),("b","Учитель B","200")})f.Services.Db.InsertLesson(new(){GroupId=id,DayOfWeek=day,Parity=0,Index=1,TimeStart="09:00",TimeEnd="10:35",SubjectRaw=Subject,SubjectNormalized="предмет",TeacherRaw=teacher,ClassroomRaw=room});
        f.Services.Homework.AddHomework(Subject,"Личный секрет A",1,createdAt:DateTime.Today);return(homework,task);
    }
    [AvaloniaFact] public async Task Subject_buttons_open_B_data_inline_without_changing_or_reading_personal_A()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","subject");var ids=SeedB(f);var shell=new ShellViewModel(f.Services);var vm=new GroupViewModel(f.Services,shell:shell);var copies=0;
        f.Intercept=(request,ct)=>Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath.EndsWith("/copies",StringComparison.Ordinal)?ReadCopies():null);
        HttpResponseMessage ReadCopies(){copies++;return Payload(new[]{new GroupHomeworkCopyResponse(ids.Task,"Задание B","Общая домашка B",1,false,0,DateTimeOffset.UtcNow.AddDays(1),ids.Homework)});}
        await vm.ActivateAsync();await vm.OpenSubjectScheduleCommand.ExecuteAsync(null);Assert.True(vm.ShowSubjectSchedule);Assert.Contains(vm.SubjectScheduleRows,x=>x.Contains("Учитель B",StringComparison.Ordinal));Assert.DoesNotContain(vm.SubjectScheduleRows,x=>x.Contains("Учитель A",StringComparison.Ordinal));
        await vm.OpenSubjectHomeworkCommand.ExecuteAsync(null);Assert.True(vm.ShowSubjectHomework);Assert.Equal("Общая домашка B",Assert.Single(vm.SubjectSharedTasks).Body);Assert.Empty(vm.SubjectHomeworks);Assert.True(copies>0);Assert.Equal("a",f.Services.Db.GetSettings().MyGroupId);Assert.Equal("Личный секрет A",Assert.Single(f.Services.Homework.GetAll()).Text);vm.Detach();shell.Detach();
    }
    [AvaloniaFact] public async Task Late_subject_copies_are_discarded_after_switching_topics_and_read_denial_purges_private_B()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","subject");var ids=SeedB(f);var other=Guid.NewGuid();f.TopicRows.Add(new(other,"Другая","user",null,null,null,0,false,permissions:["read","post"]));
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var hold=true;var denied=false;
        f.Intercept=async(request,ct)=>
        {
            if(request.RequestUri!.AbsolutePath.EndsWith("/copies",StringComparison.Ordinal)){if(hold){started.TrySetResult();await release.Task.WaitAsync(ct);}return denied?Problem(403,"forbidden"):Payload(new[]{new GroupHomeworkCopyResponse(ids.Task,"Задание B","Приватное B",1,false,0,topicId:ids.Homework)});}return null;
        };
        var opening=f.Vm.ActivateAsync();await started.Task.WaitAsync(TestContext.Current.CancellationToken);f.Vm.Channels.Single(x=>x.TopicId==other).OpenCommand.Execute(null);await Waits.Until(()=>f.Vm.SelectedChannel?.TopicId==other,"other topic selected");release.TrySetResult();await opening;Assert.Empty(f.Vm.SubjectSharedTasks);
        hold=false;f.Vm.Channels.Single(x=>x.TopicId==f.Topic).OpenCommand.Execute(null);await Waits.Until(()=>f.Vm.SubjectSharedTasks.Count==1,"B tasks loaded");denied=true;await f.Vm.OpenSubjectHomeworkCommand.ExecuteAsync(null);Assert.Empty(f.Vm.SubjectSharedTasks);Assert.Empty(f.Vm.SubjectScheduleRows);Assert.False(f.Vm.HasHome);Assert.Equal("Личный секрет A",Assert.Single(f.Services.Homework.GetAll()).Text);
    }
    [AvaloniaFact] public async Task Subject_preview_filters_linked_topics_hidden_from_the_target()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","subject");var ids=SeedB(f);
        f.Intercept=(request,ct)=>Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath.EndsWith("/space/preview",StringComparison.Ordinal)?Payload(new GroupPermissionPreviewResponse([f.TopicRows[0]])):
            request.RequestUri.AbsolutePath.EndsWith("/copies",StringComparison.Ordinal)?Payload(new[]{new GroupHomeworkCopyResponse(ids.Task,"Задание B","Не видно роли",1,true,1,topicId:ids.Homework)}):null);
        await f.Vm.ActivateAsync();await f.Vm.PreviewPersonCommand.ExecuteAsync(null);await f.Vm.OpenSubjectHomeworkCommand.ExecuteAsync(null);Assert.True(f.Vm.PreviewMode);Assert.Empty(f.Vm.SubjectSharedTasks);Assert.Empty(f.Vm.SubjectHomeworks);Assert.Equal("a",f.Services.Db.GetSettings().MyGroupId);
    }
}
