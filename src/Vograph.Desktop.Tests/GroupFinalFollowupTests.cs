using System.Net;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Vograph.Desktop.Features.Groups;
using Zapara.Contracts.Communities;
using Xunit;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;
namespace Vograph.Desktop.Tests;
public sealed class GroupFinalFollowupTests
{
    [AvaloniaFact] public async Task Archive_filter_removes_the_closed_row_and_makes_its_old_open_command_inert()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");f.ArchiveTopic();var neighbour=Guid.NewGuid();f.TopicRows.Add(new(neighbour,"Соседняя","user",null,null,null,0,false,archived:true,canPost:false,permissions:["read"]));
        await f.Vm.ActivateAsync();await f.Vm.LoadArchiveCommand.ExecuteAsync(null);var closed=f.Vm.ArchivedChannels.Single(x=>x.TopicId==f.Topic);closed.OpenCommand.Execute(null);await Waits.Until(()=>f.Vm.SelectedChannel?.TopicId==f.Topic,"archive selected");
        f.Intercept=(request,ct)=>Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath.EndsWith("/space/archive",StringComparison.Ordinal)?Payload(new GroupTopicListResponse(f.TopicRows.Where(x=>x.TopicId==neighbour).ToArray(),true)):null);
        f.Vm.Watch(true);await Waits.Until(()=>f.Vm.SelectedChannel is null&&f.Vm.ArchivedChannels.Count==1,"archive filtered",6500);
        Assert.Equal(neighbour,Assert.Single(f.Vm.ArchivedChannels).TopicId);Assert.DoesNotContain(f.Vm.ArchivedChannels,x=>x.Title=="Тема");var reads=f.MessageReads;closed.OpenCommand.Execute(null);Assert.Null(f.Vm.SelectedChannel);Assert.Equal(reads,f.MessageReads);
    }
    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task Late_homework_ack_advances_a_changed_stored_editor_after_navigation(bool existing)
    {
        using var f=new GroupSpaceViewModelTests.Fixture("homework","homework");var second=Guid.NewGuid();var homework=Guid.NewGuid();var revision=existing?3L:0L;var storedBody="v0";var calls=new List<(HttpMethod Method,HomeworkUpsert Body)>();var first=true;
        f.TopicRows.Add(new(second,"B","book",null,null,null,0,false,"homework",permissions:["read","post","homework"]));
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Intercept=async(request,ct)=>
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path.EndsWith("/copies",StringComparison.Ordinal))return Payload(request.RequestUri.Query.Contains(f.Topic.ToString("D"),StringComparison.Ordinal)&&revision>0?new[]{new GroupHomeworkCopyResponse(homework,"A",storedBody,revision,false,0,topicId:f.Topic)}:Array.Empty<GroupHomeworkCopyResponse>());
            if(path.EndsWith("/homework/share",StringComparison.Ordinal)||request.Method==HttpMethod.Put&&path.EndsWith("/homework/"+homework.ToString("D"),StringComparison.Ordinal))
            {
                var body=JsonSerializer.Deserialize<HomeworkUpsert>(await request.Content!.ReadAsStringAsync(ct),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;calls.Add((request.Method,body));
                if(first){first=false;started.TrySetResult();await release.Task.WaitAsync(ct);}Assert.Equal(revision,body.ExpectedRevision);revision++;storedBody=body.Body;
                return Payload(new HomeworkResponse(homework,CommunityId,body.Title,body.Body,revision,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,topicId:f.Topic),request.Method==HttpMethod.Post?HttpStatusCode.Created:HttpStatusCode.OK);
            }
            return null;
        };
        await f.Vm.ActivateAsync();if(existing)Assert.Single(f.Vm.ChannelHomeworks).EditCommand.Execute(null);f.Vm.SharedHomeworkTitle="A";f.Vm.SharedHomeworkBody="v1";
        var save=f.Vm.SaveSharedHomeworkCommand.ExecuteAsync(null);await started.Task.WaitAsync(TestContext.Current.CancellationToken);f.Vm.SharedHomeworkBody="v2";
        f.Vm.Channels.Single(x=>x.TopicId==second).OpenCommand.Execute(null);await Waits.Until(()=>f.Vm.SelectedChannel?.TopicId==second,"B selected");release.TrySetResult();await save;
        f.Vm.Channels.Single(x=>x.TopicId==f.Topic).OpenCommand.Execute(null);await Waits.Until(()=>f.Vm.SharedHomeworkBody=="v2","late A draft restored");await f.Vm.SaveSharedHomeworkCommand.ExecuteAsync(null);
        Assert.Equal(2,calls.Count);Assert.Equal(HttpMethod.Put,calls[1].Method);Assert.Equal(existing?4:1,calls[1].Body.ExpectedRevision);Assert.Equal("v2",calls[1].Body.Body);Assert.Equal(existing?0:1,calls.Count(x=>x.Method==HttpMethod.Post));
    }
    [AvaloniaFact] public async Task Access_selected_empty_is_detected_as_headman_but_keeps_explicit_selection_for_the_first_role()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");await f.Vm.ActivateAsync();await f.Vm.LoadAccessCommand.ExecuteAsync(null);f.Vm.SimplePostPreset=GroupViewModel.PostPresets[1];f.Vm.ApplySimpleAccessCommand.Execute(null);Assert.Equal("roles",f.Vm.SimplePostPreset.Code);
        f.Vm.SimpleAccessRoles[0].Post=true;f.Vm.ApplySimpleAccessCommand.Execute(null);Assert.Equal("allow",f.Vm.AccessRules.Single(x=>x.RoleId==f.Vm.SimpleAccessRoles[0].RoleId&&x.Power=="post").State.Code);
        f.Vm.AccessRules.Single(x=>x.RoleId==f.Vm.SimpleAccessRoles[0].RoleId&&x.Power=="post").State=SpaceAccessRow.States[0];Assert.Equal("headman",f.Vm.SimplePostPreset.Code);
    }
    [AvaloniaFact] public async Task Creation_sends_initial_rules_in_exactly_one_request_and_preserves_newer_input_after_hidden_success()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");GroupTopicRequest? sent=null;var writes=new List<string>();var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Intercept=async(request,ct)=>
        {
            if(request.Method!=HttpMethod.Get)writes.Add(request.RequestUri!.AbsolutePath);
            if(request.Method==HttpMethod.Post&&request.RequestUri!.AbsolutePath.EndsWith("/topics",StringComparison.Ordinal))
            {sent=JsonSerializer.Deserialize<GroupTopicRequest>(await request.Content!.ReadAsStringAsync(ct),new JsonSerializerOptions(JsonSerializerDefaults.Web));started.TrySetResult();await release.Task.WaitAsync(ct);return Payload(new GroupTopicListResponse(f.TopicRows.ToArray(),true),HttpStatusCode.Created);}
            return null;
        };
        await f.Vm.ActivateAsync();f.Vm.ChannelTitle="Закрытая";f.Vm.CreationReadPreset=GroupViewModel.ReadPresets[1];f.Vm.CreationPostPreset=GroupViewModel.PostPresets[1];f.Vm.CreationRoles[0].Read=true;f.Vm.CreationRoles[0].Post=true;
        var create=f.Vm.CreateChannelCommand.ExecuteAsync(null);await started.Task.WaitAsync(TestContext.Current.CancellationToken);f.Vm.ChannelTitle="Поздний ввод";release.TrySetResult();await create;
        Assert.Single(writes);Assert.EndsWith("/topics",writes[0]);Assert.NotNull(sent!.InitialAccessRules);Assert.Equal(4,sent.InitialAccessRules.Count);Assert.Contains(sent.InitialAccessRules,x=>x.RoleId is null&&x.Power=="read"&&x.State=="deny");Assert.DoesNotContain(sent.InitialAccessRules,x=>x.Power is "joins" or "exclude" or "roles" or "grants");
        Assert.Equal("Поздний ввод",f.Vm.ChannelTitle);Assert.Contains("Тема создана",f.Vm.Status);Assert.Equal(f.Topic,f.Vm.SelectedChannel!.TopicId);
    }
    [AvaloniaFact] public async Task Default_creation_omits_initial_rules_and_failure_keeps_the_whole_draft()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");string? json=null;
        f.Intercept=async(request,ct)=>{if(request.Method==HttpMethod.Post&&request.RequestUri!.AbsolutePath.EndsWith("/topics",StringComparison.Ordinal)){json=await request.Content!.ReadAsStringAsync(ct);return Problem(403,"forbidden");}return null;};
        await f.Vm.ActivateAsync();f.Vm.ChannelTitle="Сохранить ввод";f.Vm.ChannelDescription="Описание";await f.Vm.CreateChannelCommand.ExecuteAsync(null);
        Assert.NotNull(json);Assert.False(JsonDocument.Parse(json).RootElement.TryGetProperty("initialAccessRules",out _));Assert.Equal("Сохранить ввод",f.Vm.ChannelTitle);Assert.Equal("Описание",f.Vm.ChannelDescription);
    }
    [AvaloniaFact] public async Task Channels_only_cannot_submit_custom_initial_access()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");var desk=new GroupDeskResponse(false,[],[],[],[],["channels"]);
        f.Intercept=(request,ct)=>Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath.EndsWith("/space",StringComparison.Ordinal)?Payload(new GroupSpaceResponse(f.TopicRows,[],new(),desk)):request.RequestUri.AbsolutePath.EndsWith("/desk",StringComparison.Ordinal)?Payload(desk):null);
        await f.Vm.ActivateAsync();Assert.True(f.Vm.CanCreateTopic);Assert.False(f.Vm.CanSetInitialAccess);f.Vm.ChannelTitle="Закрытая";f.Vm.CreationPostPreset=GroupViewModel.PostPresets[1];var writes=f.Writes;await f.Vm.CreateChannelCommand.ExecuteAsync(null);Assert.Equal(writes,f.Writes);Assert.Contains("групповое право",f.Vm.Status);
    }
    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task Homework_ack_never_advances_a_new_editor_or_resurrects_purged_state(bool purge)
    {
        using var f=new GroupSpaceViewModelTests.Fixture("homework","homework");var id=Guid.NewGuid();var denied=false;var shares=0;var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Intercept=async(request,ct)=>
        {
            var path=request.RequestUri!.AbsolutePath;
            if(denied&&path.EndsWith("/space",StringComparison.Ordinal))return Problem(403,"forbidden");
            if(request.Method==HttpMethod.Post&&path.EndsWith("/homework/share",StringComparison.Ordinal)){shares++;var body=JsonSerializer.Deserialize<HomeworkUpsert>(await request.Content!.ReadAsStringAsync(ct),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;if(shares==1){started.TrySetResult();await release.Task.WaitAsync(ct);}return Payload(new HomeworkResponse(id,CommunityId,body.Title,body.Body,1,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow),HttpStatusCode.Created);}
            return null;
        };
        await f.Vm.ActivateAsync();f.Vm.SharedHomeworkTitle="v1";f.Vm.SharedHomeworkBody="v1";var pending=f.Vm.SaveSharedHomeworkCommand.ExecuteAsync(null);await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        if(purge){denied=true;f.Vm.Watch(true);await Waits.Until(()=>!f.Vm.HasHome,"private state purged",6500);}
        else{f.Vm.NewSharedHomeworkCommand.Execute(null);f.Vm.SharedHomeworkTitle="Новое";f.Vm.SharedHomeworkBody="Новый editor";}
        release.TrySetResult();await pending;
        if(purge){denied=false;f.Vm.RequestCommunity(CommunityId);await f.Vm.ActivateAsync();Assert.Empty(f.Vm.SharedHomeworkBody);}
        else{await f.Vm.SaveSharedHomeworkCommand.ExecuteAsync(null);Assert.Equal(2,shares);}
    }
    [AvaloniaFact] public async Task Legacy_creation_never_sends_initial_acl_and_never_falls_back_from_custom()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");string? body=null;var creations=0;
        f.Intercept=async(request,ct)=>
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path.EndsWith("/space",StringComparison.Ordinal))return Problem(404,"not_found");
            if(path.EndsWith("/topics",StringComparison.Ordinal)&&request.Method==HttpMethod.Get)return Payload(new GroupTopicListResponse(f.TopicRows,true));
            if(path.EndsWith("/topics",StringComparison.Ordinal)&&request.Method==HttpMethod.Post){creations++;body=await request.Content!.ReadAsStringAsync(ct);return Payload(new GroupTopicListResponse(f.TopicRows,true),HttpStatusCode.Created);}
            return null;
        };
        await f.Vm.ActivateAsync();Assert.False(f.Vm.CanSetInitialAccess);f.Vm.ChannelTitle="Обычная";await f.Vm.CreateChannelCommand.ExecuteAsync(null);Assert.Equal(1,creations);Assert.False(JsonDocument.Parse(body!).RootElement.TryGetProperty("initialAccessRules",out _));Assert.False(JsonDocument.Parse(body!).RootElement.TryGetProperty("template",out _));
        f.Vm.ChannelTitle="Закрытая";f.Vm.CreationReadPreset=GroupViewModel.ReadPresets[1];await f.Vm.CreateChannelCommand.ExecuteAsync(null);Assert.Equal(1,creations);Assert.Equal("Закрытая",f.Vm.ChannelTitle);
    }
    [AvaloniaFact] public async Task Creation_selected_zero_roles_keeps_intent_and_allows_picking_the_first_role()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");GroupTopicRequest? sent=null;f.Intercept=async(request,ct)=>{if(request.Method==HttpMethod.Post&&request.RequestUri!.AbsolutePath.EndsWith("/topics",StringComparison.Ordinal)){sent=JsonSerializer.Deserialize<GroupTopicRequest>(await request.Content!.ReadAsStringAsync(ct),new JsonSerializerOptions(JsonSerializerDefaults.Web));return Problem(403,"forbidden");}return null;};
        await f.Vm.ActivateAsync();f.Vm.ChannelTitle="Тема";f.Vm.CreationPostPreset=GroupViewModel.PostPresets[1];await f.Vm.CreateChannelCommand.ExecuteAsync(null);Assert.Equal("roles",f.Vm.CreationPostPreset.Code);Assert.Single(sent!.InitialAccessRules!);
        f.Vm.CreationRoles[0].Post=true;await f.Vm.CreateChannelCommand.ExecuteAsync(null);Assert.Contains(sent!.InitialAccessRules!,x=>x.RoleId==f.Vm.CreationRoles[0].RoleId&&x.Power=="post"&&x.State=="allow");
        await f.Vm.ActivateAsync();Assert.Equal("roles",f.Vm.CreationPostPreset.Code);Assert.True(f.Vm.CreationRoles[0].Post);
    }

}
