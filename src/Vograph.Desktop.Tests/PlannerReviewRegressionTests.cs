using System.Net;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Features.Groups;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Shell;
using Zapara.Contracts.Communities;
using Xunit;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;
namespace Vograph.Desktop.Tests;
public sealed class PlannerReviewRegressionTests
{
    [Theory][InlineData(401)][InlineData(403)][InlineData(404)][InlineData(0)]
    public async Task Authorization_denials_remove_shared_deadlines_but_keep_local_data(int status)
    {
        using var db=TestDb.Create(false);var shell=new ShellViewModel(db.Services);using var handler=new AccountClientHandler();using var http=new HttpClient(handler);using var api=new CommunityHttpClient(http,Root);
        var denied=false;var group=db.Services.Db.GetGroup(TestDb.MyGroupId)!.Name;
        db.Services.Homework.AddHomework(TestDb.MathSubject,"Локальное",1,createdAt:new DateTime(2026,9,13));
        db.Services.UseCommunities(api,_=>denied&&status==0?Task.FromException<string?>(new AccountClientException(AccountClientFailure.InvalidSession)):Task.FromResult<string?>(Access));
        handler.Send=(request,ct)=>Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/communities",StringComparison.Ordinal)?Payload(new[]{new CommunityResponse(CommunityId,group,"",1,"member")}):denied?Problem(status,status==401?"invalid_session":status==403?"forbidden":"not_found"):Payload(new[]{new GroupHomeworkCopyResponse(Guid.NewGuid(),TestDb.MathSubject,"Общее закрытое",1,false,0,new DateTimeOffset(2026,9,15,8,0,0,TimeSpan.Zero))}));
        var vm=new ScheduleViewModel(db.Services,shell,()=>new DateTime(2026,9,14));await vm.InitializeAsync();Assert.Contains(vm.Deadlines,x=>x.Key.StartsWith("shared:",StringComparison.Ordinal));
        denied=true;await vm.ReloadAsync();Assert.DoesNotContain(vm.Deadlines,x=>x.Key.StartsWith("shared:",StringComparison.Ordinal));Assert.Single(db.Services.Homework.GetAll());vm.Detach();shell.Detach();
    }
    [Fact] public async Task A_transport_failure_after_group_change_cannot_restore_the_previous_groups_private_deadlines()
    {
        using var db=TestDb.Create(false);var shell=new ShellViewModel(db.Services);using var handler=new AccountClientHandler();using var http=new HttpClient(handler);using var api=new CommunityHttpClient(http,Root);var failed=false;var group=db.Services.Db.GetGroup(TestDb.MyGroupId)!.Name;
        db.Services.UseCommunities(api,_=>Task.FromResult<string?>(Access));handler.Send=(request,ct)=>failed?Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")):Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/communities",StringComparison.Ordinal)?Payload(new[]{new CommunityResponse(CommunityId,group,"",1,"member")}):Payload(new[]{new GroupHomeworkCopyResponse(Guid.NewGuid(),"Предмет","Старой группы",1,false,0,new DateTimeOffset(2026,9,15,8,0,0,TimeSpan.Zero))}));
        var vm=new ScheduleViewModel(db.Services,shell,()=>new DateTime(2026,9,14));await vm.InitializeAsync();Assert.Single(vm.Deadlines);
        db.Services.Db.UpsertGroup(new(){Id="other",Name="Другая"});var settings=db.Services.Db.GetSettings();settings.MyGroupId="other";db.Services.Db.SaveSettings(settings);failed=true;await vm.ReloadAsync();Assert.Empty(vm.Deadlines);vm.Detach();shell.Detach();
    }
    [Fact] public async Task Stable_shared_rows_open_and_discuss_the_latest_payload()
    {
        using var db=TestDb.Create(false);var shell=new ShellViewModel(db.Services);using var handler=new AccountClientHandler();using var http=new HttpClient(handler);using var api=new CommunityHttpClient(http,Root);db.Services.UseCommunities(api,_=>Task.FromResult<string?>(Access));
        var group=db.Services.Db.GetGroup(TestDb.MyGroupId)!.Name;var homework=Guid.NewGuid();var hwTopic=Guid.NewGuid();var chatTopic=Guid.NewGuid();var conversation=Guid.NewGuid();var body="Старый текст";var deadline=new DateTimeOffset(2026,9,15,8,0,0,TimeSpan.Zero);var desk=new GroupDeskResponse(false,[],[],[],[],[]);
        handler.Send=(request,ct)=>
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path.EndsWith("/communities",StringComparison.Ordinal))return Task.FromResult(Payload(new[]{new CommunityResponse(CommunityId,group,"",1,"member")}));
            if(path.EndsWith("/copies",StringComparison.Ordinal))return Task.FromResult(Payload(new[]{new GroupHomeworkCopyResponse(homework,TestDb.MathSubject,body,1,false,0,deadline,hwTopic)}));
            if(path.EndsWith("/home",StringComparison.Ordinal))return Task.FromResult(Payload(new GroupHomeResponse(CommunityId,group,group,new(conversation,"group",CommunityId,group,null,null,null,0),[new(AccountClientTestSupport.UserId,"anya","Аня","member",true)],[])));
            if(path.EndsWith("/space",StringComparison.Ordinal))return Task.FromResult(Payload(new GroupSpaceResponse([new(hwTopic,"Домашка","book",null,null,null,0,false,"homework",permissions:["read"]),new(chatTopic,"Чат","user",null,null,null,0,false,permissions:["read","post"])],[],new(),desk)));
            if(path.EndsWith("/desk",StringComparison.Ordinal))return Task.FromResult(Payload(desk));
            if(path.EndsWith("/messages",StringComparison.Ordinal))return Task.FromResult(Payload(new ChatPageResponse([],false)));
            return Task.FromResult(Problem(404,"not_found"));
        };
        var vm=new ScheduleViewModel(db.Services,shell,()=>new DateTime(2026,9,14));await vm.InitializeAsync();var row=Assert.Single(vm.Deadlines);body="Новый текст";deadline=deadline.AddDays(1);await vm.ReloadAsync();Assert.Same(row,Assert.Single(vm.Deadlines));
        await row.OpenCommand.ExecuteAsync(null);var groupVm=shell.Section<GroupViewModel>(SectionKey.Group);await Waits.Until(()=>groupVm.ShowSingleHomework&&groupVm.ChannelHomeworks.Count==1,"latest shared homework opened");Assert.Equal("Новый текст",groupVm.ChannelHomeworks[0].Body);
        row.DiscussCommand.Execute(null);await Waits.Until(()=>groupVm.HasDiscussionContext,"latest discussion");Assert.Contains("Новый текст",groupVm.DiscussionContext);Assert.Contains("16.09.2026",groupVm.DiscussionContext);vm.Detach();groupVm.Detach();shell.Detach();
    }
    [Fact] public void Group_projection_distinguishes_missing_copy_and_the_period_boundary()
    {
        using var db=TestDb.Create(false);db.Services.Db.UpsertGroup(new(){Id="other",Name="Другая"});var settings=db.Services.Db.GetSettings();settings.PeriodStart="2026-09-14";db.Services.Db.SaveSettings(settings);
        var before=new ScheduleComposer(db.Services,"other").Compose(0,new DateTime(2026,9,7));Assert.True(before.IsUnavailable);Assert.Contains("вне известного",before.EmptyTitle);
        Assert.Null(before.Dates!.First(x=>x.Date.Date==new DateTime(2026,9,7)).LessonCount);
    }
}
